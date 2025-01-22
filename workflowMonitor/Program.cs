using System.ServiceProcess;
using Microsoft.Data.SqlClient;
using Utilities_NetCore;

namespace workflowMonitorService
{
    public class WorkflowMonitor : ServiceBase
    {
        private Task _monitoringTask = Task.CompletedTask;
        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        public static SqlConnection connection = new SqlConnection();
        public static string programName = "WorkflowMonitor";

        public WorkflowMonitor()
        {
            ServiceName = programName;
        }

        protected override void OnStart(string[] args)
        {
            _cancellationTokenSource = new CancellationTokenSource();
            _monitoringTask = Task.Run(() => MonitorEvents(_cancellationTokenSource.Token));
        }

        protected override void OnStop()
        {
            _cancellationTokenSource.Cancel();
            _monitoringTask?.Wait();
            if (connection.State != System.Data.ConnectionState.Closed)
            {
                connection.Close();
            }
        }

        private async Task MonitorEvents(CancellationToken cancellationToken)
        {
            var logMethod = modLogging.eLogMethod.DATABASE;
            try
            {
#if DEBUG
                string? connectionString = Environment.GetEnvironmentVariable("ConnectionStringDebug");
#else
                string? connectionString = Environment.GetEnvironmentVariable("ConnectionStringRelease");
#endif
                if (connectionString == null)
                {
                    modLogging.AddLog(programName, "C#", "Program.MonitorEvents", modLogging.eLogLevel.CRITICAL, "Unable to read connection string", logMethod);
                    return;
                }

                connection.ConnectionString = connectionString;

                // only want this to run between 2:30am and 10:30pm, the remaining 4 hours are a nightly maintenance period
                TimeSpan startTime = new(2, 30, 0);
                TimeSpan endTime = new(22, 30, 0);

                while (!cancellationToken.IsCancellationRequested)
                {
                    if (canExecuteEvents(startTime, endTime))
                    {
                        // check if database connection is open, if not, open it
                        if (connection.State != System.Data.ConnectionState.Open)
                        {
                            connection.Open();
                        }

                        // query database for events that need to be started
                        var pendingEvents = new List<clsEvent>();
                        var command = new SqlCommand
                        {
                            Connection = connection,
                            CommandType = System.Data.CommandType.StoredProcedure,
                            CommandText = "Workflow.dbo.pendingEvents"
                        };

                        using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                        {
                            while (await reader.ReadAsync(cancellationToken))
                            {
                                var eventRecord = new clsEvent
                                {
                                    eventID = Convert.ToInt32(reader["eventID"]),
                                    actionID = Convert.ToInt32(reader["actionID"]),
                                    applicationFilename = reader["applicationFilename"] as string,
                                    applicationDefaultParameter = reader["applicationDefaultParameter"] != DBNull.Value ? reader["applicationDefaultParameter"] as string : null,
                                    eventParameters = reader["eventParameters"] != DBNull.Value ? reader["eventParameters"] as string : null,
                                    actionLogOutput = Convert.ToBoolean(reader["actionLogOutput"]),
                                    applicationType = reader["applicationType"] != DBNull.Value ? reader["applicationType"] as string : null
                                };

                                pendingEvents.Add(eventRecord);
                            }
                        }

                        // iterate through event records
                        foreach (clsEvent evt in pendingEvents)
                        {
                            command.Parameters.Clear();
                            command.CommandText = "Workflow.dbo.canStartEvent";
                            command.Parameters.AddWithValue("@eventID", evt.eventID);

                            var result = await command.ExecuteScalarAsync(cancellationToken);
                            if (result != DBNull.Value && Convert.ToInt16(result) == 1)
                            {
                                await clsActions.StartApplication(evt);
                            }
                        }
                    }

                    // sleep for the defined period or until cancellation is requested
                    int sleepSeconds = 10;
                    await Task.Delay(1000 * sleepSeconds, cancellationToken);
                }
            }
            catch (Exception ex)
            {
                modLogging.AddLog(programName, "C#", "Program.MonitorEvents", modLogging.eLogLevel.CRITICAL, $"{ex.Message} --- {ex.StackTrace}", logMethod);
            }
        }

        private bool canExecuteEvents(TimeSpan startTime, TimeSpan endTime)
        {
#if DEBUG
            return true;
#else
            TimeSpan currentTime = DateTime.Now.TimeOfDay;
            return currentTime >= startTime && currentTime <= endTime;
#endif
        }

        public static void Main()
        {
            ServiceBase.Run(new WorkflowMonitor());
        }
    }
}
