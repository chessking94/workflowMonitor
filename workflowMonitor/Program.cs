using Microsoft.Data.SqlClient;

namespace workflowMonitor
{
    class Program
    {
        public static Utilities_NetCore.clsConfig myConfig = new Utilities_NetCore.clsConfig();
        public static SqlConnection connection = new SqlConnection();

        static async Task Main(string[] args)
        {
#if DEBUG
            Console.WriteLine("You are running this in DEBUG mode and the process will never terminate!");
            Console.WriteLine("If you are not stepping through an active debug session, please be advised to do so.");
            Console.WriteLine("If you wish to continue, please type any key.");
            Console.ReadKey();
#endif

            // set reference variables
            string projectDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..\\..\\.."));
            string configFile = Path.Combine(projectDir, "appsettings.json");
            myConfig.configFile = configFile;

#if DEBUG
            string connectionString = myConfig.getConfig("connectionStringDev");
#else
            string connectionString = myConfig.getConfig("connectionStringProd");
#endif
            connection.ConnectionString = connectionString;

            List<Task> tasks = new List<Task>();

            TimeSpan startTime = new TimeSpan(8, 0, 0);
            TimeSpan endTime = new TimeSpan(20, 0, 0);

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
                command.CommandText = "dbo.pendingEvents";

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

                        pendingEvents.Add(eventRecord);
                    }
                }
                command.Dispose();

                // iterate through event records
                command.Connection = connection;
                command.CommandType = System.Data.CommandType.StoredProcedure;
                command.CommandText = "dbo.canStartEvent";
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
                int sleepSeconds = 60;
                Thread.Sleep(1000 * sleepSeconds);
                executeEvents = canExecuteEvents(startTime, endTime);
#endif
            }

            await Task.WhenAll(tasks);

            // clean-up
            if (connection.State == System.Data.ConnectionState.Open)
            {
                connection.Close();
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
