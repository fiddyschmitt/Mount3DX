using libCommon;
using Microsoft.Win32;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Edge;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;

namespace lib3dx
{
    public static class _3dxLogin
    {
        public static (bool Success, string Cookies) GetSessionCookies(string loginUrl)
        {
            IWebDriver? driver = null;

            try
            {
                driver = GetDriverForDefaultBrowser();

                var wait = new WebDriverWait(driver, TimeSpan.FromMinutes(2));

                driver.Navigate().GoToUrl(loginUrl);

                wait.Until(d => d.Manage().Cookies.GetCookieNamed("SERVERID") != null);
                var serverId = driver.Manage().Cookies.GetCookieNamed("SERVERID").Value;

                wait.Until(d => d.Manage().Cookies.GetCookieNamed("JSESSIONID") != null);
                var jessionId = driver.Manage().Cookies.GetCookieNamed("JSESSIONID").Value;

                var result = $"SERVERID={serverId}; JSESSIONID={jessionId}";

                return (true, result);
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error in GetSessionCookies:{Environment.NewLine}{ex}");
                return (false, ex.Message);
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

            return browser switch
            {
                "chrome" => new ChromeDriver(),
                "firefox" => new FirefoxDriver(),
                "edge" => new EdgeDriver(),
                _ => throw new Exception("Unsupported browser."),
            };
        }

        static string? GetDefaultBrowser()
        {
            using RegistryKey? userChoiceKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\http\UserChoice");
            if (userChoiceKey == null)
            {
                throw new Exception("Failed to get default browser.");
            }

            var progIdValue = userChoiceKey?.GetValue("Progid");
            if (progIdValue == null)
            {
                throw new Exception("Failed to get default browser.");
            }

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
