using CsvHelper.Configuration;
using CsvHelper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.DevTools;
using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace URLMatch
{
    internal class Program
    {
        static async Task Main(string[] args)
        {

            if (args.Length != 1)
            {
                Console.WriteLine("Usage: URLMatch.exe url_list.txt");
                return;
            }

            Console.WriteLine("--------------------------\nUrl Redirection Validator\n--------------------------\n");

            string urlFilePath = args[0];
            string csvFilePath = "result.csv";

            List<string> redirectionURL = new List<string>();
            List<string> destinationURL = new List<string>();

            string url = string.Empty;

            try
            {
                Console.WriteLine("\nReading URLs from the text file\n");
                string[] urls = File.ReadAllLines(urlFilePath);

                bool firstLine = true;
                foreach (string line in urls)
                {
                    if (firstLine)
                    {
                        firstLine = false;
                        continue; 
                    }

                    string[] parts = line.Split(',');
                    redirectionURL.Add(parts[0].Trim());
                    destinationURL.Add(parts[1].Trim());
                }

                if (redirectionURL.Count != destinationURL.Count)
                {
                    throw new Exception("The number of redirection URLs must be the same as the destination URLs.");
                }

                Console.WriteLine("\nURLs successfully read.\n");
                int urlNumber = 0;
                int totalItems = urls.Length-1;
                int currentItem = 0;

                Console.WriteLine("\nStarting Chrome web driver.\n");
                ChromeOptions options = SetWebDriver();
                ChromeDriver driver = CreateDriver(options);
                DevToolsSession session = await StartSession(driver);

                for (int i = 0; i < redirectionURL.Count; i++)
                {
                    url = redirectionURL[i];
                    urlNumber = i;

                    await InitEventListener(url, csvFilePath, session, driver, urlNumber, redirectionURL, destinationURL);
                    await Task.Delay(100);

                    currentItem++;

                    Console.WriteLine($"\nProcessed URLs: {currentItem} / {totalItems}\n");
                }

                Console.WriteLine($"\nAll URLs have been successfully processed.\n");


            }
            catch (Exception e)
            {
                Console.WriteLine($"An error occurred: {e.Message}");
            }
        }

        static ChromeOptions SetWebDriver()
        {
            ChromeOptions options = new ChromeOptions();
            options.AddArgument("--disable-features=IsolateOrigins,site-per-process");
            options.AddArgument("disable-features=NetworkService");
            options.AddArgument("--disable-web-security");
            options.AddArgument("--allow-running-insecure-content");
            options.AddArgument("--disable-extensions");
            options.AddArgument("--ignore-certificate-errors");
            options.AddArgument("--disable-notifications");
            options.AddArgument("--disable-popup-blocking");
            options.AddArgument("--enable-chrome-browser-cloud-management");
            options.AddArgument("--disable-usb-device-redirector");
            options.AddExcludedArguments("excludeSwitches", "enable-logging");
            //options.AddArgument("--headless");

            return options;
        }

        static ChromeDriver CreateDriver(ChromeOptions options)
        {
            var driver = new ChromeDriver(options);

            return driver;
        }

        static async Task<DevToolsSession> StartSession(ChromeDriver driver)
        {
            IDevTools devTools = driver;
            DevToolsSession session = devTools.GetDevToolsSession();
            await session.Domains.Network.EnableNetwork();

            return session;
        }

        static async Task InitEventListener(string url, string csvFilePath, DevToolsSession session, ChromeDriver driver, int urlNumber, List<string> redirectionURL, List<string> destinationURL)
        {
            bool firstRequest = false;
            string firstRequestId = string.Empty;
            bool stopSession = false;

            Dictionary<string, List<JObject>> dataToExcelRequest = new Dictionary<string, List<JObject>>();
            Dictionary<string, List<JObject>> dataToExcelResponse = new Dictionary<string, List<JObject>>();


            session.DevToolsEventReceived += (sender, e) =>
            {
                if (dataToExcelRequest.Count == 0 && dataToExcelResponse.Count == 0)
                {
                    firstRequest = true;
                }
                else
                {
                    firstRequest = false;
                }

                if (e.EventName == "requestWillBeSentExtraInfo" && !stopSession)
                {
                    JObject jsonObjectRequest = JObject.Parse(e.EventData.ToString());
                    JObject resultObjectRequest = new JObject();
                    JObject jsonObjectRequestHeaders = JObject.Parse(jsonObjectRequest["headers"].ToString());

                    resultObjectRequest["requestId"] = jsonObjectRequest["requestId"];
                    resultObjectRequest["authority"] = jsonObjectRequestHeaders[":authority"];
                    resultObjectRequest["path"] = jsonObjectRequestHeaders[":path"];
                    resultObjectRequest["Action"] = "Request";



                    string resultJsonRequest = resultObjectRequest.ToString(Formatting.Indented);

                    if (firstRequest == false && firstRequestId != resultObjectRequest["requestId"]?.ToString())
                    {
                        stopSession = true;
                    }
                    else
                    {
                        if (dataToExcelRequest.ContainsKey(resultObjectRequest["requestId"].ToString()))
                        {
                            dataToExcelRequest[resultObjectRequest["requestId"].ToString()].Add(JObject.Parse(resultJsonRequest));
                        }
                        else
                        {
                            dataToExcelRequest.Add(resultObjectRequest["requestId"].ToString(), new List<JObject> { JObject.Parse(resultJsonRequest) });
                        }

                        // Validate if is the first request
                        if (firstRequest)
                        {
                            firstRequestId = resultObjectRequest["requestId"].ToString();
                        }
                    }
                }


                if (e.EventName == "responseReceivedExtraInfo" && !stopSession)
                {

                    JObject jsonObjectResponse = JObject.Parse(e.EventData.ToString());
                    JObject resultObjectResponse = new JObject();
                    JObject jsonObjectResponseHeaders = JObject.Parse(jsonObjectResponse["headers"].ToString());

                    resultObjectResponse["requestId"] = jsonObjectResponse["requestId"];
                    resultObjectResponse["statusCode"] = jsonObjectResponse["statusCode"];
                    resultObjectResponse["Action"] = "Response";

                    string resultJsonResponse = resultObjectResponse.ToString(Formatting.Indented);

                    if (dataToExcelResponse.ContainsKey(resultObjectResponse["requestId"].ToString()))
                    {
                        dataToExcelResponse[resultObjectResponse["requestId"].ToString()].Add(JObject.Parse(resultJsonResponse));
                    }
                    else
                    {
                        dataToExcelResponse.Add(resultObjectResponse["requestId"].ToString(), new List<JObject> { JObject.Parse(resultJsonResponse) });
                    }
                }

            };

            driver.Navigate().GoToUrl(url);
            await Task.Delay(3000);
            processingData(dataToExcelRequest, dataToExcelResponse, csvFilePath, urlNumber, redirectionURL, destinationURL);

        }

        static void processingData(Dictionary<string, List<JObject>> dataToExcelRequest, Dictionary<string, List<JObject>> dataToExcelResponse, string csvFilePath, int urlNumber, List<string> redirectionURL, List<string> destinationURL)
        {
            Dictionary<string, Dictionary<string, string>> requestData = new Dictionary<string, Dictionary<string, string>>();

            foreach (var requestId in dataToExcelRequest.Keys)
            {
                var requestList = dataToExcelRequest[requestId];

                var requestDict = new Dictionary<string, string>();
                requestDict.Add("redirectionURL", redirectionURL[urlNumber]);
                requestDict.Add("destinationURL", destinationURL[urlNumber]);

                var lastRequest = requestList.LastOrDefault();
                var finalDestinationUrl = lastRequest?["authority"]?.ToString() + lastRequest?["path"]?.ToString();

                var finalDomainUrl = GetDomainFromUrl(finalDestinationUrl);
                var destinationDomainUrl = GetDomainFromUrl(destinationURL[urlNumber]);

                var finalStatus = finalDomainUrl == destinationDomainUrl ? "Success" : "Failure";
                requestDict.Add("finalDestinationUrl", finalDestinationUrl);
                requestDict.Add("finalStatus", finalStatus);

                requestData.Add(requestId, requestDict);
            }

            WriteDictionaryToCsv(requestData, csvFilePath);
        }

        static string GetDomainFromUrl(string url)
        {
            string domain = string.Empty;
            Regex regex = new Regex(@"^(?:https?:\/\/)?(?:[^@\n]+@)?(?:www\.)?([^:\/\n?]+)");

            Match match = regex.Match(url);
            if (match.Success)
            {
                domain = match.Groups[1].Value;
            }

            return domain;
        }

        static void WriteDictionaryToCsv(Dictionary<string, Dictionary<string, string>> dictionary, string csvFilePath)
        {
            Console.WriteLine("\nRegistering information in the CSV file.\n");
            var existsFile = File.Exists(csvFilePath);

            var csvConfig = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = ","
            };

            using (var writer = new StreamWriter(csvFilePath, append: true))
            using (var csv = new CsvWriter(writer, csvConfig))
            {
                if (!existsFile)
                {
                    csv.WriteField("Request Id");
                    csv.WriteField("Redirection URL");
                    csv.WriteField("Destination URL");
                    csv.WriteField("Final Destination URL");
                    csv.WriteField("Final Status");
                    csv.NextRecord();
                }

                foreach (var key in dictionary.Keys)
                {
                    var requestData = dictionary[key];
                    var redirectionUrl = requestData.GetValueOrDefault("redirectionURL", "");
                    var destinationUrl = requestData.GetValueOrDefault("destinationURL", "");
                    var finalDestinationUrl = requestData.GetValueOrDefault("finalDestinationUrl", "");
                    var finalStatus = requestData.GetValueOrDefault("finalStatus", "");

                    csv.WriteField(key);
                    csv.WriteField(redirectionUrl);
                    csv.WriteField(destinationUrl);
                    csv.WriteField(finalDestinationUrl);
                    csv.WriteField(finalStatus);
                    csv.NextRecord();
                } 
            }
        }

    }
}
