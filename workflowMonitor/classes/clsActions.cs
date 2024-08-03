using Microsoft.Data.SqlClient;
using System.Diagnostics;

namespace workflowMonitor
{
    internal class clsActions
    {
        static async Task StartApplication(clsEvent myEvent)
        {
            try
            {
                var tcs = new TaskCompletionSource<int>();
                Process process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = myEvent.applicationFilename,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    },
                    EnableRaisingEvents = true
                };

                if (myEvent.eventParameters != null)
                {
                    process.StartInfo.Arguments = myEvent.eventParameters;
                }

                process.Exited += (sender, args) =>
                {
                    tcs.SetResult(process.ExitCode);
                    process.Dispose();
                };

                process.Start();
                UpdateEventStatus(myEvent.eventID, "Processing");

                // read the output if needed
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                int exitCode = await tcs.Task;

                if (exitCode == 0)
                {
                    UpdateEventStatus(myEvent.eventID, "Complete");
                }
                else
                {
                    UpdateEventStatus(myEvent.eventID, "Error", error);
                }
            }
            catch (Exception ex)
            {
                UpdateEventStatus(myEvent.eventID, "Error", ex.Message);
            }
        }

        private static void UpdateEventStatus(int eventID, string eventStatus, string? eventError = null)
        {
            var command = new SqlCommand();
            command.Connection = Program.connection;
            command.CommandType = System.Data.CommandType.StoredProcedure;
            command.CommandText = "dbo.updateEventStatus";
            command.Parameters.AddWithValue("@eventID", eventID);
            command.Parameters.AddWithValue("@eventStatus", eventStatus);
            if (eventError != null)
            {
                command.Parameters.AddWithValue("@eventError", eventError);
            }
            command.ExecuteNonQuery();  // TODO: do I need to change this to ExecuteNonQueryAsync?
        }
    }
}
