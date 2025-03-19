using libCommon;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.WebUtilities;
using System.Web;

namespace lib3dx
{
    public static class _3dxLogin
    {
        public static bool LogInUsingSelenium(string loginUrl, HttpClient client, CookieContainer cookieContainer)
        {
            IWebDriver? driver = null;

            try
            {
                driver = GetDriverForDefaultBrowser();

                var wait = new WebDriverWait(driver, TimeSpan.FromMinutes(2));

                //get the cookies for the 3dspace service
                driver.Navigate().GoToUrl(loginUrl);

                wait.Until(d => d.Manage().Cookies.GetCookieNamed("SERVERID") != null);
                wait.Until(d => d.Manage().Cookies.GetCookieNamed("JSESSIONID") != null);

                driver
                    .Manage()
                    .Cookies
                    .AllCookies
                    .ToList()
                    .ForEach(cookie =>
                    {
                        cookieContainer.Add(cookie.ToCookie());
                    });


                //now get the cookies for the search service
                var searchServiceUrl = DiscoverSearchServiceUrl(loginUrl, client);

                driver.Navigate().GoToUrl(searchServiceUrl);

                wait.Until(d => d.Manage().Cookies.GetCookieNamed("SERVERID") != null);
                wait.Until(d => d.Manage().Cookies.GetCookieNamed("JSESSIONID") != null);

                driver
                    .Manage()
                    .Cookies
                    .AllCookies
                    .ToList()
                    .ForEach(cookie =>
                    {
                        cookieContainer.Add(cookie.ToCookie());
                    });

                return true;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error in {nameof(LogInUsingSelenium)}:{Environment.NewLine}{ex}");
                return false;
            }
            finally
            {
                try
                {
                    driver?.Quit();
                }
                catch { }
            }
        }

        static IWebDriver GetDriverForDefaultBrowser()
        {
            var browser = GetDefaultBrowser();

            if (browser == "firefox")
            {
                var service = FirefoxDriverService.CreateDefaultService();
                service.HideCommandPromptWindow = true;

                var options = new FirefoxOptions();
                var result = new FirefoxDriver(service, options);
                return result;
            }

            return browser switch
            {
                "chrome" => new ChromeDriver(),
                "edge" => new EdgeDriver(),
                _ => throw new Exception("Unsupported browser."),
            };
        }

        static string? GetDefaultBrowser()
        {
            using RegistryKey? userChoiceKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice") ?? throw new Exception("Failed to get default browser.");

            var progIdValue = (userChoiceKey?.GetValue("Progid")) ?? throw new Exception("Failed to get default browser.");
            var progIdLower = progIdValue?.ToString()?.ToLower() ?? "unknown";
            if (progIdLower.Contains("chrome"))
            {
                return "chrome";
            }
            else if (progIdLower.Contains("firefox"))
            {
                return "firefox";
            }
            else if (progIdLower.Contains("edge"))
            {
                return "edge";
            }

            return null;
        }

        public static bool LogInUsingHttpClient(string loginUrl, HttpClient client, HttpClientHandler clientHandler)
        {
            try
            {
                var cookies = new CookieContainer();
                var handler = new HttpClientHandler
                {
                    CookieContainer = cookies,
                };

                //load the certificates the server may request
                var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);

                var certs = store.Certificates
                                .Find(X509FindType.FindByExtension, "2.5.29.37", false)     //Enhanced Key Usage
                                .OfType<X509Certificate2>()
                                .Where(cert => cert.Extensions.OfType<X509EnhancedKeyUsageExtension>()
                                                .Any(eku => eku.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == "1.3.6.1.4.1.311.20.2.2")     //Smart Card Log-on
                                                         && eku.EnhancedKeyUsages.Cast<Oid>().Any(oid => oid.Value == "1.3.6.1.5.5.7.3.2")          //Client Authentication
                                ))
                                .Where(cert => cert.IssuerName.Name.Contains("Hardware Issuing CA"))
                                .ToArray();
                clientHandler.ClientCertificates.AddRange(certs);


                

                //go to the login page
                var request = new HttpRequestMessage(HttpMethod.Get, loginUrl);
                request.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:128.0) Gecko/20100101 Firefox/128.0");

                var response = client.Send(request);
                response.EnsureSuccessStatusCode();
                var responseStr = response.Content.ReadAsStringAsync().Result;



                //get the SAMLRequest
                var doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(responseStr);
                var samlRequest = doc.DocumentNode.SelectSingleNode($"//input[@name='SAMLRequest']").GetAttributeValue("value", "");
                var samlUrl = doc.DocumentNode.SelectSingleNode("//form")?.GetAttributeValue("action", "") ?? "";
                samlUrl = HttpUtility.HtmlDecode(samlUrl);

                //post to the SAML URL
                var postRequestContent = new FormUrlEncodedContent([
                    new KeyValuePair<string, string>("SAMLRequest", samlRequest)
                ]);

                var postResponse = client.PostAsync(samlUrl, postRequestContent).Result;
                responseStr = postResponse.Content.ReadAsStringAsync().Result;

                //to get HttpUtility.ParseQueryString to get the first query para 
                var resumeFullUrl = postResponse.RequestMessage?.RequestUri?.ToString().Replace("?", "&") ?? throw new Exception($"Could not retrieve resumeFullUrl");

                var resumeUrl = HttpUtility.ParseQueryString(resumeFullUrl)["resumeUrl"] ?? throw new Exception($"Could not retrieve resumeUrl");
                var resumePath = HttpUtility.ParseQueryString(resumeFullUrl)["resumePath"] ?? throw new Exception($"Could not retrieve resumePath");
                var instanceId = HttpUtility.ParseQueryString(resumeFullUrl)["instanceId"] ?? throw new Exception($"Could not retrieve instanceId");
                var assuranceLevel = HttpUtility.ParseQueryString(resumeFullUrl)["assuranceLevel"] ?? throw new Exception($"Could not retrieve assuranceLevel");

                doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(responseStr);
                var scripts = doc.DocumentNode.SelectNodes("//script").Select(node => node.GetAttributeValue("src", "")).ToList();

                //put together the url used to authenticate with Secure Badge
                var gasBaseUri = postResponse.RequestMessage?.RequestUri ?? throw new Exception($"Could not retrieve gasBaseUri");

                var gasBaseUrl = $"{gasBaseUri.Scheme}://{gasBaseUri.Host}{gasBaseUri.AbsolutePath}";
                var internalSecureBadgeAuth = scripts
                                                    .AsParallel()
                                                    .Select(script =>
                                                    {
                                                        var scriptUrl = gasBaseUrl.UrlCombine(script);
                                                        var req = new HttpRequestMessage(HttpMethod.Get, scriptUrl);
                                                        var resp = client.Send(req);
                                                        resp.EnsureSuccessStatusCode();

                                                        var scriptText = resp.Content.ReadAsStringAsync().Result;
                                                        return scriptText;
                                                    })
                                                    .FirstOrDefault(scriptText => scriptText.Contains("Tn_INTERNAL_SECURE_BADGE_AUTH"))?
                                                    .Split(["Tn_INTERNAL_SECURE_BADGE_AUTH=\""], StringSplitOptions.None)?
                                                    .LastOrDefault()?
                                                    .Split(["\""], StringSplitOptions.None)
                                                    .FirstOrDefault() ?? throw new Exception($"Could not retrieve Tn_INTERNAL_SECURE_BADGE_AUTH from script.");

                var configUrl = gasBaseUrl.UrlCombine("configuration/internal");
                request = new HttpRequestMessage(HttpMethod.Get, configUrl);

                response = client.Send(request);
                response.EnsureSuccessStatusCode();
                responseStr = response.Content.ReadAsStringAsync().Result;

                var secureBadgeAuthPort = JObject.Parse(responseStr)["configuration"]?["internal.secure_badge_auth_port"]?.Value<int>();
                var badgeCertUrl = $"{gasBaseUri.Scheme}://{gasBaseUri.Host}:{secureBadgeAuthPort}".UrlCombine(internalSecureBadgeAuth);








                var queryParams = new Dictionary<string, string?>
                {
                    { "resumeUrl", resumeUrl },
                    { "resumePath", resumePath },
                    { "instanceId", instanceId },
                    { "pingfedDropoff", "true" },
                    { "assuranceLevel", assuranceLevel },
                    { "errorUrl", gasBaseUrl },
                    { "gasWebClient",  "true"},
                    { "coordinates",  "null"},
                    { "countryCodeRequested",  "false"},
                    { "usaItarGeoRequested",  "false"}
                };

                badgeCertUrl = QueryHelpers.AddQueryString(badgeCertUrl, queryParams);

                //authenticate using the Secure Badge certificate
                request = new HttpRequestMessage(HttpMethod.Get, badgeCertUrl);


                //this will prompt for Smart Card PIN
                response = client.Send(request);
                response.EnsureSuccessStatusCode();
                var getResponseStr = response.Content.ReadAsStringAsync().Result;




                //get the SAMLResponse and finish logging in
                doc = new();
                doc.LoadHtml(getResponseStr);
                var samlResponse = doc.DocumentNode.SelectSingleNode($"//input[@name='SAMLResponse']").GetAttributeValue("value", "");
                var postToUrl = doc.DocumentNode.SelectSingleNode("//form")?.GetAttributeValue("action", "") ?? "";

                var postRequest = new HttpRequestMessage(HttpMethod.Post, postToUrl)
                {
                    Content = new FormUrlEncodedContent([
                        new KeyValuePair<string, string>("SAMLResponse", samlResponse)
                    ])
                };

                postResponse = client.Send(postRequest);
                postResponse.EnsureSuccessStatusCode();
                responseStr = postResponse.Content.ReadAsStringAsync().Result;



                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string DiscoverSearchServiceUrl(string loginUrl, HttpClient client)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, loginUrl.UrlCombine("resources/AppsMngt/api/v1/services"));

            var response = client.Send(request);
            response.EnsureSuccessStatusCode();
            var servicesJson = response.Content.ReadAsStringAsync().Result;

            var searchService = (JObject
                                    .Parse(servicesJson)?["platforms"]?[0]?["services"]?
                                    .Select(service => new
                                    {
                                        Id = service["id"]?.ToString(),
                                        Name = service["name"]?.ToString(),
                                        Url = service["url"]?.ToString()
                                    })
                                    .FirstOrDefault(service => service.Name == "3DSearch")) ?? throw new Exception("Could not discover 3DSearch service.");

            if (string.IsNullOrEmpty(searchService.Url))
            {
                throw new Exception("Could not discover URL for 3DSearch service.");
            }

            return searchService.Url;
        }

        public static System.Net.Cookie ToCookie(this OpenQA.Selenium.Cookie seleniumCookie)
        {
            var result = new System.Net.Cookie(
                            seleniumCookie.Name,
                            seleniumCookie.Value,
                            seleniumCookie.Path,
                            seleniumCookie.Domain)
            {
                Secure = seleniumCookie.Secure,
                HttpOnly = seleniumCookie.IsHttpOnly
            };

            if (seleniumCookie.Expiry.HasValue)
            {
                result.Expires = seleniumCookie.Expiry.Value;
            }

            return result;
        }
    }
}
