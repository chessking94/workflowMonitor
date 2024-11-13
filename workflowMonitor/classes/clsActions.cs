using Microsoft.Data.SqlClient;
using System.Diagnostics;

namespace workflowMonitor
{
    internal class clsActions
    {
        public static async Task StartApplication(clsEvent myEvent)
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

                if (!string.IsNullOrWhiteSpace(myEvent.applicationDefaultParameter))
                {
                    process.StartInfo.Arguments = myEvent.applicationDefaultParameter;
                }

                if (!string.IsNullOrWhiteSpace(myEvent.eventParameters))
                {
                    if (!string.IsNullOrWhiteSpace(process.StartInfo.Arguments))
                    {
                        process.StartInfo.Arguments += " ";
                    }
                    process.StartInfo.Arguments += myEvent.eventParameters;
                }

                process.Exited += (sender, args) =>
                {
                    tcs.SetResult(process.ExitCode);
                };

                process.Start();
                UpdateEventStatus(myEvent.eventID, "Processing");

                // read the output if needed
                string output = await process.StandardOutput.ReadToEndAsync();
                output = output.Trim();

                string error = await process.StandardError.ReadToEndAsync();
                error = error.Trim();

                int exitCode = await tcs.Task;
                process.Dispose();

                if (exitCode == 0)
                {
                    UpdateEventStatus(myEvent.eventID, "Complete", myEvent.actionLogOutput ? output : null);
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

        private static void UpdateEventStatus(int eventID, string eventStatus, string? eventNote = null)
        {
#if DEBUG
            Debug.Print(eventStatus);
#else
            var command = new SqlCommand();
            command.Connection = Program.connection;
            command.CommandType = System.Data.CommandType.StoredProcedure;
            command.CommandText = "Workflow.dbo.updateEventStatus";
            command.Parameters.AddWithValue("@eventID", eventID);
            command.Parameters.AddWithValue("@eventStatus", eventStatus);
            if (eventNote != null)
            {
                command.Parameters.AddWithValue("@eventNote", eventNote);
            }
            command.ExecuteNonQuery();
#endif
        }
    }
}
