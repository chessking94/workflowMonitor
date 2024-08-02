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

                // read the output if needed
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                int exitCode = await tcs.Task;

                if (exitCode == 0)
                {
                    // TODO: update the database
                    Console.WriteLine($"Application {myEvent.applicationFilename} completed successfully");
                }
                else
                {
                    // TODO: update the database
                    Console.WriteLine($"Application {myEvent.applicationFilename} returned an error: {error}");
                }
            }
            catch (Exception ex)
            {
                // TODO: update the database
                Console.WriteLine($"Error starting application {myEvent.applicationFilename}: {ex.Message}");
            }
        }

        internal void UpdateEventStatus(int eventID, string eventStatus)
        {
            // TODO: may need to update this proc to update the eventError field as well
            var command = new SqlCommand();
            command.Connection = Program.connection;
            command.CommandType = System.Data.CommandType.StoredProcedure;
            command.CommandText = "dbo.updateEventStatus";
            command.Parameters.AddWithValue("@eventID", eventID);
            command.Parameters.AddWithValue("@eventStatus", eventStatus);
            command.ExecuteNonQuery();
        }
    }
}
