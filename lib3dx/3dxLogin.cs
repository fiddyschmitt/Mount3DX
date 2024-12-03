using libCommon;
using Microsoft.Win32;
using Newtonsoft.Json.Linq;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;
using System.Net;

namespace lib3dx
{
    public static class _3dxLogin
    {
        public static (bool Success, _3dxCookies? Cookies) GetSessionCookies(string loginUrl)
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
    }
}
