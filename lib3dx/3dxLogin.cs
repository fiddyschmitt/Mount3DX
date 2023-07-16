using libCommon;
using OpenQA.Selenium;
using OpenQA.Selenium.Firefox;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace lib3dx
{
    public static class _3dxLogin
    {
        public static (bool Success, string Cookies) GetSessionCookies(string loginUrl)
        {
            FirefoxDriver? driver = null;

            try
            {
                var driverService = FirefoxDriverService.CreateDefaultService();
                driverService.HideCommandPromptWindow = true;
                driver = new FirefoxDriver(driverService, new FirefoxOptions()
                {
                    AcceptInsecureCertificates = true
                });

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
    }
}
