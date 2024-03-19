using CsvHelper.Configuration;
using CsvHelper;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.DevTools;
using System;
using System.Globalization;
using System.Runtime.InteropServices;

namespace URLMatch
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            
            //if (args.Length != 1)
            //{
            //    Console.WriteLine("Usage: URLMatch.exe url_list.txt");
            //    return;
            //}

            Console.WriteLine("--------------------------\nUrl Redirection Validator\n--------------------------\n");

            //string urlFilePath = args[0];
            string urlFilePath = "../../../url_list.txt";
            //string csvFilePath = "result.csv";
            string csvFilePath = "../../../result.csv";

            try
            {
                ChromeOptions options = SetWebDriver();
                ChromeDriver driver = CreateDriver(options);
                DevToolsSession session = await StartSession(options, driver);

                string[] urls = File.ReadAllLines(urlFilePath);

                string url = string.Empty;
                int totalItems = urls.Length;
                int currentItem = 0;

                foreach (var item in urls)
                {
                    url = item;
                    await initEventListener(options, url, csvFilePath, session, driver);
                    await Task.Delay(100);

                    currentItem++;

                    Console.WriteLine($"\nProcessed URLs: {currentItem} / {totalItems}\n");

                }

                
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
            options.AddArguments("disable-features=NetworkService");
            options.AddArguments("--disable-web-security");
            options.AddArguments("--allow-running-insecure-content");
            options.AddArguments("--disable-extensions");
            options.AddArguments("--ignore-certificate-errors");
            options.AddArguments("--disable-notifications");
            options.AddArguments("--disable-popup-blocking");
            options.AddArguments("--enable-chrome-browser-cloud-management");

            //options.AddArgument("--headless");

            return options;
        }

        static ChromeDriver CreateDriver(ChromeOptions options)
        {
            var driver = new ChromeDriver(options);

            return driver;
        }

        static async Task<DevToolsSession> StartSession(ChromeOptions options, ChromeDriver driver)
        {
            IDevTools devTools = driver;
            DevToolsSession session = devTools.GetDevToolsSession();
            await session.Domains.Network.EnableNetwork();

            return session;
        }

        static async Task initEventListener(ChromeOptions options, string url, string csvFilePath, DevToolsSession session, ChromeDriver driver)
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
            processingData(dataToExcelRequest, dataToExcelResponse, csvFilePath);

        }



        static void processingData(Dictionary<string, List<JObject>> dataToExcelRequest, Dictionary<string, List<JObject>> dataToExcelResponse, string csvFilePath)
        {
            Dictionary<string, List<JObject>> combinedDictionary = new Dictionary<string, List<JObject>>();


            HashSet<string> allKeys = new HashSet<string>(dataToExcelRequest.Keys.Concat(dataToExcelResponse.Keys));

            foreach (var key in allKeys)
            {
                List<JObject> requestJObjects = dataToExcelRequest.ContainsKey(key) ? dataToExcelRequest[key] : new List<JObject>();

                List<JObject> responseJObjects = dataToExcelResponse.ContainsKey(key) ? dataToExcelResponse[key] : new List<JObject>();

                List<JObject> combinedJObjects = InterleaveLists(requestJObjects, responseJObjects);

                combinedDictionary.Add(key, combinedJObjects);
            }

            WriteDictionaryToCsv(combinedDictionary, csvFilePath);
        }

        static List<JObject> InterleaveLists(List<JObject> list1, List<JObject> list2)
        {
            List<JObject> interleavedList = new List<JObject>();
            int maxLength = Math.Max(list1.Count, list2.Count);

            for (int i = 0; i < maxLength; i++)
            {
                if (i < list1.Count)
                {
                    interleavedList.Add(list1[i]);
                }
                if (i < list2.Count)
                {
                    interleavedList.Add(list2[i]);
                }
            }

            return interleavedList;
        }

        static void WriteDictionaryToCsv(Dictionary<string, List<JObject>> dictionary, string csvFilePath)
        {
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
                    csv.WriteField("Action");
                    csv.WriteField("URL");
                    csv.WriteField("Status Code");
                    csv.NextRecord();
                }

                var allKeys = dictionary.Keys.ToList();

                foreach (var key in allKeys)
                {
                    var jObjects = dictionary[key];

                    foreach (var jObject in jObjects)
                    {
                        var action = jObject["Action"].ToString();
                        var url = action == "Request" ? $"{jObject["authority"]}{jObject["path"]}" : "";
                        var statusCode = action == "Response" ? jObject["statusCode"]?.ToString() : "";

                        csv.WriteField(key);
                        csv.WriteField(action);
                        csv.WriteField(url);
                        csv.WriteField(statusCode);
                        csv.NextRecord();
                    }
                }
            }
        }



    }
}
