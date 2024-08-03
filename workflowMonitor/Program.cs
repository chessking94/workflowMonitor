using Microsoft.Data.SqlClient;
// using System.Threading.Tasks;

namespace workflowMonitor
{
    class Program
    {
        public static Utilities_NetCore.clsConfig myConfig = new Utilities_NetCore.clsConfig();
        public static SqlConnection connection = new SqlConnection();

        static async Task Main(string[] args)
        {
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

                        eventRecord.eventID = reader["eventID"] != DBNull.Value ? Convert.ToInt32(reader["eventID"]) : default;
                        eventRecord.actionID = reader["actionID"] != DBNull.Value ? Convert.ToInt32(reader["actionID"]) : default;
                        eventRecord.applicationFilename = reader["applicationFilename"] != DBNull.Value ? reader["applicationFilename"] as string : null;
                        eventRecord.eventParameters = reader["eventParameters"] != DBNull.Value ? reader["eventParameters"] as string : null;

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
                        // TODO: how do I want to determine what action to run? log something in the DB? Hard-code the action ID's?
                        
                        // TODO: event can be started, start it asyncronously
                    }
                }
                command.Dispose();

                // TODO: when event ends/errors, need to update DB

                // sleep for the defined period then re-populate executeEvents to exit loop when necessary
                int sleepSeconds = 60;
                Thread.Sleep(1000 * sleepSeconds);
                executeEvents = canExecuteEvents(startTime, endTime);

                // TODO: confirm if I'm processing async I'm able to exit this loop while events are still running
            }

            // clean-up
            if (connection.State == System.Data.ConnectionState.Open)
            {
                connection.Close();
            }

            //string query = "SELECT COUNT(*) FROM YourTableName WHERE Status = 'New'"; // Replace with your actual query
            //string applicationPath = "YourApplicationPathHere"; // Replace with the path to the application you want to start

            //while (true)
            //{
            //    if (await CheckForNewRecordsAsync(connectionString, query))
            //    {
            //        StartApplication(applicationPath);
            //    }
            //    else
            //    {
            //        Console.WriteLine($"{DateTime.Now}: No new records found.");
            //    }

            //    await Task.Delay(60000); // Wait for 1 minute before checking again
            //}
        }

        //static async Task<bool> CheckForNewRecordsAsync(string connectionString, string query)
        //{
        //    using (SqlConnection connection = new SqlConnection(connectionString))
        //    {
        //        await connection.OpenAsync();

        //        using (SqlCommand command = new SqlCommand(query, connection))
        //        {
        //            int recordCount = (int)await command.ExecuteScalarAsync();
        //            return recordCount > 0;
        //        }
        //    }
        //}

        //static void StartApplication(string applicationPath)
        //{
        //    try
        //    {
        //        Process.Start(new ProcessStartInfo
        //        {
        //            FileName = applicationPath,
        //            UseShellExecute = true
        //        });
        //        Console.WriteLine($"{DateTime.Now}: Application started successfully.");
        //    }
        //    catch (Exception ex)
        //    {
        //        Console.WriteLine($"{DateTime.Now}: Failed to start application: {ex.Message}");
        //    }
        //}

        static Boolean canExecuteEvents(TimeSpan startTime, TimeSpan endTime)
        {
            TimeSpan currentTime = DateTime.Now.TimeOfDay;
            if (currentTime >= startTime && currentTime <= endTime)
            {
                return true;
            }
            else
            {
                return false;
            }
        }
    }

}
