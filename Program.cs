// part of source code copied from https://zimmergren.net/building-custom-data-collectors-for-azure-log-analytics/

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace LogAnalyticsAPIUpload
{
    internal class Program
    {
        private static readonly string ApiVersion = "2016-04-01";
        private static HttpClient httpClient;
        private static string WorkspaceId { get; set; }
        private static string SharedKey { get; set; }
        private static string RequestBaseUrl { get; set; }

        public static async Task SendLogEntry<T>(T entity, string logType)
        {
            #region Argument validation

            if (entity == null)
                throw new NullReferenceException("parameter 'entity' cannot be null");

            if (logType.Length > 100)
                throw new ArgumentOutOfRangeException(nameof(logType), logType.Length,
                    "The size limit for this parameter is 100 characters.");

            if (!IsAlphaOnly(logType))
                throw new ArgumentOutOfRangeException(nameof(logType), logType,
                    "Log-Type can only contain alpha characters. It does not support numerics or special characters.");

            ValidatePropertyTypes(entity);

            #endregion

            var list = new List<T> { entity };
            await SendLogEntries(list, logType).ConfigureAwait(false);
        }

        public static async Task SendLogEntries<T>(List<T> entities, string logType)
        {
            #region Argument validation

            if (entities == null)
                throw new NullReferenceException("parameter 'entities' cannot be null");

            if (logType.Length > 100)
                throw new ArgumentOutOfRangeException(nameof(logType), logType.Length,
                    "The size limit for this parameter is 100 characters.");

            if (!IsAlphaOnly(logType))
                throw new ArgumentOutOfRangeException(nameof(logType), logType,
                    "Log-Type can only contain alpha characters. It does not support numerics or special characters.");

            foreach (var entity in entities)
                ValidatePropertyTypes(entity);

            #endregion

            var dateTimeNow = DateTime.UtcNow.ToString("r");

            var entityAsJson = JsonConvert.SerializeObject(entities);
            var authSignature = GetAuthSignature(entityAsJson, dateTimeNow);

            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("Authorization", authSignature);
            httpClient.DefaultRequestHeaders.Add("Log-Type", logType);
            httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            httpClient.DefaultRequestHeaders.Add("x-ms-date", dateTimeNow);
            httpClient.DefaultRequestHeaders.Add("time-generated-field",
                ""); // if we want to extend this in the future to support custom date fields from the entity etc.

            HttpContent httpContent = new StringContent(entityAsJson, Encoding.UTF8);
            httpContent.Headers.ContentType = new MediaTypeHeaderValue("application/json"); 
            var response = await httpClient.PostAsync(new Uri(RequestBaseUrl), httpContent).ConfigureAwait(false);

            var responseContent = response.Content; 
            var result = await responseContent.ReadAsStringAsync().ConfigureAwait(false);
            // helpful todo: if you want to return the data, this might be a good place to start working with it...
        }

        public static void Main(string[] args)
        {
            Console.WriteLine(
                "#  |  _  _    /\\  _  _ | _|_. _ _   /\\ |~)~|~  | | _ | _  _  _|\r\n#  |_(_)(_|  /~~\\| |(_||\\/| |(__\\  /~~\\|~ _|_  |_||_)|(_)(_|(_|\r\n#        _|             /                         |            \r\n\r\n");


            httpClient = new HttpClient();


            Console.Write("Enter Workspace id: ");
            WorkspaceId = Console.ReadLine();

            Console.Write("Enter Shared key: ");
            SharedKey = Console.ReadLine();

            RequestBaseUrl = $"https://{WorkspaceId}.ods.opinsights.azure.com/api/logs?api-version={ApiVersion}";


            var dd = "";
            while (true)
            {
                SendCommandsMessage();
                dd = Console.ReadLine();
                //Console.WriteLine("you wrote this:" + dd);
                var command = 0;
                var cmdOK = int.TryParse(dd, out command);
                if (!cmdOK)
                {
                    Console.WriteLine("Try again..");
                    continue;
                }

                switch (command)
                {
                    case 1:
                        Console.WriteLine("Sending User log x 5");
                        for (var i = 0; i < 5; i++)
                            SendLogEntry(new LogAnalyticsLogEvent
                            {
                                EventDateTimeUTC = DateTime.UtcNow,
                                EventType = "Login",
                                UserID = Guid.NewGuid().ToString(),
                                LocationId = Guid.NewGuid().ToString()
                            }, "logtable").ConfigureAwait(false);

                        Console.WriteLine("Sent");

                        continue;
                    case 2:
                        Console.WriteLine("Sending Open File log x 5");
                        for (var i = 0; i < 5; i++)
                            SendLogEntry(new LogAnalyticsLogEvent
                            {
                                EventDateTimeUTC = DateTime.UtcNow,
                                EventType = "Open File",
                                UserID = Guid.NewGuid().ToString(),
                                FileName = Guid.NewGuid().ToString()
                            }, "logtable").ConfigureAwait(false);
                        Console.WriteLine("Sent");
                        continue;
                    default:
                        Console.WriteLine("Unknown command. Please enter next command");
                        SendCommandsMessage();
                        continue;
                }
            }
        }

        #region Helpers

        private static void SendCommandsMessage()
        {
            Console.WriteLine("======================================");
            Console.WriteLine("Type following numbers to send demo log entries");

            Console.WriteLine("Enter 1 for: Send demo login event");
            Console.WriteLine("Enter 2 for: Send demo open file event");
        }

        private static string GetAuthSignature(string serializedJsonObject, string dateString)
        {
            var stringToSign =
                $"POST\n{serializedJsonObject.Length}\napplication/json\nx-ms-date:{dateString}\n/api/logs";
            string signedString;

            var encoding = new ASCIIEncoding();
            var sharedKeyBytes = Convert.FromBase64String(SharedKey);
            var stringToSignBytes = encoding.GetBytes(stringToSign);
            using (var hmacsha256Encryption = new HMACSHA256(sharedKeyBytes))
            {
                var hashBytes = hmacsha256Encryption.ComputeHash(stringToSignBytes);
                signedString = Convert.ToBase64String(hashBytes);
            }

            return $"SharedKey {WorkspaceId}:{signedString}";
        }

        private static bool IsAlphaOnly(string str)
        {
            return Regex.IsMatch(str, @"^[a-zA-Z]+$");
        }

        private static void ValidatePropertyTypes<T>(T entity)
        {
            // as of 2018-10-30, the allowed property types for log analytics, as defined here (https://docs.microsoft.com/en-us/azure/log-analytics/log-analytics-data-collector-api#record-type-and-properties) are: string, bool, double, datetime, guid.
            // anything else will be throwing an exception here.
            foreach (var propertyInfo in entity.GetType().GetProperties())
                if (propertyInfo.PropertyType != typeof(string) &&
                    propertyInfo.PropertyType != typeof(bool) &&
                    propertyInfo.PropertyType != typeof(double) &&
                    propertyInfo.PropertyType != typeof(DateTime) &&
                    propertyInfo.PropertyType != typeof(Guid))
                    throw new ArgumentOutOfRangeException(
                        $"Property '{propertyInfo.Name}' of entity with type '{entity.GetType()}' is not one of the valid properties. Valid properties are String, Boolean, Double, DateTime, Guid.");
        }

        #endregion
    }

    public class LogAnalyticsLogEvent
    {
        public DateTime EventDateTimeUTC { get; set; }
        public string EventType { get; set; }
        public string UserID { get; set; }
        public string LocationId { get; set; }
        public string FileName { get; set; }
    }
}