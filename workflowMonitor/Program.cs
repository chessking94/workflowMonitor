using Microsoft.Data.SqlClient;
using System.Diagnostics;
using Utilities_NetCore;

namespace workflowMonitor
{
    class Program
    {
        public static SqlConnection connection = new SqlConnection();
        public static string programName = "workflowMonitor";

        static async Task Main(string[] args)
        {
            if (args.Length != 0)
            {
                // argument was passed, only check if program is already running
                var processes = Process.GetProcessesByName(programName);
                if (processes.Length <= 1)
                {
                    // The only instance of workflowMonitor.exe running is the status check, something is wrong
                    modNotifications.SendTelegramMessage("WARNING: workflowMonitor.exe is not running!");
                }
            }
            else
            {
#if DEBUG
                Console.WriteLine("NOTICE: You are running this in DEBUG mode and the process will only execute one iteration.");
                Console.WriteLine("If you wish to continue, please type any key.");
                Console.ReadKey();

                var logMethod = modLogging.eLogMethod.CONSOLE;
#else
                var logMethod = modLogging.eLogMethod.DATABASE;
                modLogging.AddLog(programName, "C#", "Program.Main", modLogging.eLogLevel.INFO, "Process started", logMethod);
#endif

                try
                {
#if DEBUG
                    // three directories up from exe
                    string projectDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\.."));
                    string? connectionString = Environment.GetEnvironmentVariable("ConnectionStringDebug");
#else
                    // one directory up from exe
                    string projectDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".."));
                    string? connectionString = Environment.GetEnvironmentVariable("ConnectionStringRelease");
#endif
                    if (connectionString == null)
                    {
                        modLogging.AddLog(programName, "C#", "Program.Main", modLogging.eLogLevel.CRITICAL, "Unable to read connection string", logMethod);
                        Environment.Exit(-1);
                    }

                    connection.ConnectionString = connectionString;

                    var tasks = new List<Task>();

                    TimeSpan startTime = new TimeSpan(1, 55, 0);
                    TimeSpan endTime = new TimeSpan(22, 30, 0);

                    Boolean executeEvents = canExecuteEvents(startTime, endTime);
                    while (executeEvents)
                    {
                        // check if database connection is open, if not, open it
                        if (connection.State != System.Data.ConnectionState.Open)
                        {
                            connection.Open();
                        }

                        // query database for events that need to be started (proc dbo.pendingEvents)
                        var pendingEvents = new List<clsEvent>();

                        var command = new SqlCommand();
                        command.Connection = connection;
                        command.CommandType = System.Data.CommandType.StoredProcedure;
                        command.CommandText = "Workflow.dbo.pendingEvents";

                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var eventRecord = new clsEvent();

                                eventRecord.eventID = Convert.ToInt32(reader["eventID"]);
                                eventRecord.actionID = Convert.ToInt32(reader["actionID"]);
                                eventRecord.applicationFilename = reader["applicationFilename"] as string;
                                eventRecord.applicationDefaultParameter = reader["applicationDefaultParameter"] != DBNull.Value ? reader["applicationDefaultParameter"] as string : null;
                                eventRecord.eventParameters = reader["eventParameters"] != DBNull.Value ? reader["eventParameters"] as string : null;
                                eventRecord.actionLogOutput = Convert.ToBoolean(reader["actionLogOutput"]);
                                eventRecord.applicationType = reader["applicationType"] != DBNull.Value ? reader["applicationType"] as string : null;

                                pendingEvents.Add(eventRecord);
                            }
                        }
                        command.Dispose();

                        // iterate through event records
                        command.Connection = connection;
                        command.CommandType = System.Data.CommandType.StoredProcedure;
                        command.CommandText = "Workflow.dbo.canStartEvent";
                        foreach (clsEvent evt in pendingEvents)
                        {
                            // determine if an event can be started
                            command.Parameters.Clear();
                            command.Parameters.AddWithValue("@eventID", evt.eventID);
                            var result = command.ExecuteScalar();
                            if (result != DBNull.Value && Convert.ToInt16(result) == 1)
                            {
                                tasks.Add(clsActions.StartApplication(evt));
                            }
                        }
                        command.Dispose();

#if DEBUG
                        // only want to run one iteration of this loop in testing
                        break;
#else
                        // sleep for the defined period then re-populate executeEvents to exit loop when necessary
                        int sleepSeconds = 10;
                        Thread.Sleep(1000 * sleepSeconds);
                        executeEvents = canExecuteEvents(startTime, endTime);
#endif
                    }

                    await Task.WhenAll(tasks);
                }
                catch (Exception ex)
                {
                    modLogging.AddLog(programName, "C#", "Program.Main", modLogging.eLogLevel.CRITICAL, $"{ex.Message} --- {ex.StackTrace}", logMethod);
                }
                finally
                {
                    // clean-up
                    if (connection.State == System.Data.ConnectionState.Open)
                    {
                        connection.Close();
                    }
                }
#if !DEBUG
                modLogging.AddLog(programName, "C#", "Program.Main", modLogging.eLogLevel.INFO, "Process ended", logMethod);
#endif
            }
        }

        static Boolean canExecuteEvents(TimeSpan startTime, TimeSpan endTime)
        {
#if DEBUG
            return true;
#else
            TimeSpan currentTime = DateTime.Now.TimeOfDay;
            if (currentTime >= startTime && currentTime <= endTime)
            {
                return true;
            }
            else
            {
                return false;
            }
#endif
        }
    }
}
