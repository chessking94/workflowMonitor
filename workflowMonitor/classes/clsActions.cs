using Microsoft.Data.SqlClient;
using System.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Utilities_NetCore;

namespace workflowMonitorService
{
    internal class clsActions
    {
        public static async Task StartApplication(clsEvent myEvent)
        {
            try
            {
                switch (myEvent.applicationType)
                {
                    case "Python Script":
                    case "Batch Script":
                    case "Executable":
                        await StartCommand(myEvent);
                        break;
                    case "Stored Procedure":
                        await StartProcedure(myEvent);
                        break;
                    default:
                        // this shouldn't ever happen, write a log record
                        modLogging.AddLog(WorkflowMonitor.programName, "C#", "clsActions.StartApplication", modLogging.eLogLevel.ERROR, $"Invalid applicationType in event #{myEvent.eventID}: <{myEvent.applicationType}>", modLogging.eLogMethod.DATABASE);
                        UpdateEventStatus(myEvent.eventID, "Error", $"Invalid applicationType: <{myEvent.applicationType}>");
                        break;
                }
            }
            catch (Exception ex)
            {
                UpdateEventStatus(myEvent.eventID, "Error", ex.Message);
            }
        }

        private static async Task StartCommand(clsEvent myEvent)
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

            UpdateEventStatus(myEvent.eventID, "Processing");
            process.Start();

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

        private static async Task StartProcedure(clsEvent myEvent)
        {
            var command = new SqlCommand();
            command.Connection = WorkflowMonitor.connection;
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = myEvent.applicationFilename;

            if (!String.IsNullOrWhiteSpace(myEvent.eventParameters))
            {
                // expect parameters it to be in proper form, i.e. @Parameter1 = 'Value1', @Parameter2 = 'Value2', etc
                // @(\w +) -> matches to the parameter name, e.g. @Parameter1
                // s*=\s* -> matches to the equals symbol, with and without spaces around it
                // (?:'([^']*)'|([^,]+)) -> matches to the value, with and without single-quotes
                string pattern = @"@(\w+)\s*=\s*(?:'([^']*)'|([^,]+))";
                Regex regex = new Regex(pattern);

                foreach (Match match in regex.Matches(myEvent.eventParameters))
                {
                    string parameterName = $"@{match.Groups[1].Value}";
                    string parameterValue = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;

                    command.Parameters.AddWithValue(parameterName, parameterValue);
                }
            }

            SqlParameter returnParameter = new SqlParameter
            {
                ParameterName = "@ReturnValue",
                SqlDbType = SqlDbType.Int,
                Direction = ParameterDirection.ReturnValue
            };
            command.Parameters.Add(returnParameter);

            UpdateEventStatus(myEvent.eventID, "Processing");

            await command.ExecuteNonQueryAsync();

            int returnValue = (int)returnParameter.Value;
            if (returnValue == 0)
            {
                UpdateEventStatus(myEvent.eventID, "Complete");  // TODO: figure out if there is anything I want to have returned for successful sproc notes
            }
            else
            {
                UpdateEventStatus(myEvent.eventID, "Error", "Stored procedure failed");  // TODO: figure out what I need to return to have a proper error message
            }
        }

        private static void UpdateEventStatus(int eventID, string eventStatus, string? eventNote = null)
        {
            var command = new SqlCommand();
            command.Connection = WorkflowMonitor.connection;
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "Workflow.dbo.updateEventStatus";
            command.Parameters.AddWithValue("@eventID", eventID);
            command.Parameters.AddWithValue("@eventStatus", eventStatus);
            if (eventNote != null)
            {
                command.Parameters.AddWithValue("@eventNote", eventNote);
            }
            command.ExecuteNonQuery();
        }
    }
}
