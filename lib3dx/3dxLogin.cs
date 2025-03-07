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
        public static (bool Success, _3dxCookies? Cookies) GetSessionCookies(string loginUrl)
        {
            //Try to log in using Single Sign-On
            var result = GetSessionCookiesUsingHttpClient(loginUrl);

            //Fall back to Selenium
            if (!result.Success)
            {
                result = GetSessionCookiesUsingSelenium(loginUrl);
            }

            return result;
        }


        public static (bool Success, _3dxCookies? Cookies) GetSessionCookiesUsingSelenium(string loginUrl)
        {
            IWebDriver? driver = null;

            try
            {
                driver = GetDriverForDefaultBrowser();

                var wait = new WebDriverWait(driver, TimeSpan.FromMinutes(2));

                //get the cookies for the 3dspace service

                driver.Navigate().GoToUrl(loginUrl);

                wait.Until(d => d.Manage().Cookies.GetCookieNamed("SERVERID") != null);
                var serverId = driver.Manage().Cookies.GetCookieNamed("SERVERID").Value;

                wait.Until(d => d.Manage().Cookies.GetCookieNamed("JSESSIONID") != null);
                var jessionId = driver.Manage().Cookies.GetCookieNamed("JSESSIONID").Value;

                var _3dSpaceCookie = new _3dxCookie()
                {
                    BaseUrl = loginUrl,
                    Cookie = $"SERVERID={serverId}; JSESSIONID={jessionId}"
                };


                //we now need to discover the search service
                var discoverServicesUrl = loginUrl.UrlCombine("resources/AppsMngt/api/v1/services");
                var servicesJson = libCommon.Utilities.WebUtility.HttpGet(discoverServicesUrl, _3dSpaceCookie.Cookie);

                var searchService = (JObject
                                        .Parse(servicesJson)?["platforms"]?[0]?["services"]
                                        ?.Select(service => new
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

                //now get the cookies for the search service

                driver.Navigate().GoToUrl(searchService.Url);

                wait.Until(d => d.Manage().Cookies.GetCookieNamed("SERVERID") != null);
                serverId = driver.Manage().Cookies.GetCookieNamed("SERVERID").Value;

                wait.Until(d => d.Manage().Cookies.GetCookieNamed("JSESSIONID") != null);
                jessionId = driver.Manage().Cookies.GetCookieNamed("JSESSIONID").Value;

                var _3dSearchCookie = new _3dxCookie()
                {
                    BaseUrl = searchService.Url,
                    Cookie = $"SERVERID={serverId}; JSESSIONID={jessionId}"
                };


                var result = new _3dxCookies()
                {
                    _3DSpace = _3dSpaceCookie,
                    _3DSearch = _3dSearchCookie,
                };

                return (true, result);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error in GetSessionCookies:{Environment.NewLine}{ex}");
                return (false, null);
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

        public static (bool Success, _3dxCookies? Cookies) GetSessionCookiesUsingHttpClient(string loginUrl)
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
                handler.ClientCertificates.AddRange(certs);





                var client = new HttpClient(handler);

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
                samlUrl = System.Web.HttpUtility.HtmlDecode(samlUrl);

                //post to the SAML URL
                var postRequestContent = new FormUrlEncodedContent([
                    new KeyValuePair<string, string>("SAMLRequest", samlRequest)
                ]);

                var postResponse = client.PostAsync(samlUrl, postRequestContent).Result;
                responseStr = postResponse.Content.ReadAsStringAsync().Result;

                var resumeFullUrl = postResponse.RequestMessage.RequestUri.ToString().Replace("?", "&");    //to get HttpUtility.ParseQueryString to get the first query param
                var resumeUrl = HttpUtility.ParseQueryString(resumeFullUrl)["resumeUrl"];
                var resumePath = HttpUtility.ParseQueryString(resumeFullUrl)["resumePath"];
                var instanceId = HttpUtility.ParseQueryString(resumeFullUrl)["instanceId"];
                var assuranceLevel = HttpUtility.ParseQueryString(resumeFullUrl)["assuranceLevel"];

                doc = new HtmlAgilityPack.HtmlDocument();
                doc.LoadHtml(responseStr);
                var scripts = doc.DocumentNode.SelectNodes("//script").Select(node => node.GetAttributeValue("src", "")).ToList();

                //put together the url used to authenticate with Secure Badge
                var gasBaseUri = postResponse.RequestMessage.RequestUri;
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
                                                    .Split(new[] { "Tn_INTERNAL_SECURE_BADGE_AUTH=\"" }, StringSplitOptions.None)?
                                                    .LastOrDefault()?
                                                    .Split(new[] { "\"" }, StringSplitOptions.None)
                                                    .FirstOrDefault();

                var configUrl = gasBaseUrl.UrlCombine("configuration/internal");
                request = new HttpRequestMessage(HttpMethod.Get, configUrl);

                response = client.Send(request);
                response.EnsureSuccessStatusCode();
                responseStr = response.Content.ReadAsStringAsync().Result;

                var secureBadgeAuthPort = JObject.Parse(responseStr)["configuration"]?["internal.secure_badge_auth_port"]?.Value<int>();
                var badgeCertUrl = $"{gasBaseUri.Scheme}://{gasBaseUri.Host}:{secureBadgeAuthPort}".UrlCombine(internalSecureBadgeAuth);








                var queryParams = new Dictionary<string, string>
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






                //we now need to discover the search service
                request = new HttpRequestMessage(HttpMethod.Get, loginUrl.UrlCombine("resources/AppsMngt/api/v1/services"));

                response = client.Send(request);
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

                //visit the search service to acquire cookies
                request = new HttpRequestMessage(HttpMethod.Get, searchService.Url);

                response = client.Send(request);
                //response.EnsureSuccessStatusCode();

                //var allCookiesStr = cookies.GetAllCookies().SerializeToJson();


                var result = new _3dxCookies()
                {
                    _3DSpace = new _3dxCookie()
                    {
                        BaseUrl = loginUrl,
                        Cookie = cookies
                                    .GetAllCookies()
                                    .Where(cookie => string.Equals(cookie.Domain, new Uri(loginUrl)?.Host))
                                    .Where(cookie => cookie.Name == "JSESSIONID" || cookie.Name == "SERVERID")
                                    .Select(cookie => $"{cookie.Name}={cookie.Value}")
                                    .ToString("; ")
                    },
                    _3DSearch = new _3dxCookie()
                    {
                        BaseUrl = searchService.Url,
                        Cookie = cookies
                                    .GetAllCookies()
                                    .Where(cookie => string.Equals(cookie.Domain, new Uri(searchService.Url)?.Host))
                                    .Where(cookie => cookie.Name == "JSESSIONID" || cookie.Name == "SERVERID")
                                    .Select(cookie => $"{cookie.Name}={cookie.Value}")
                                    .ToString("; ")
                    }
                };

                return (true, result);
            }
            catch (Exception)
            {
                return (false, null);
            }
        }
    }
}
