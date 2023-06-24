using libCommon;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace lib3dx
{
    public class _3dxFile : _3dxItem
    {
        public _3dxFile(string fileRevision, string documentObjectId)
        {
            FileRevision = fileRevision;
            DocumentObjectId = documentObjectId;
        }

        public string FileRevision;
        string DocumentObjectId;
        public ulong Size;

        public MemoryStream Download(string serverUrl, string cookies)
        {
            //get download token
            var objectUrl = serverUrl.UrlCombine(@$"resources/v1/application/CSRF");

            var request = new HttpRequestMessage()
            {
                RequestUri = new Uri(objectUrl),
                Method = HttpMethod.Get
            };

            var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Cookie", cookies);

            var downloadTokenJson = client.SendAsync(request).Result.Content.ReadAsStringAsync().Result;
            var downloadToken = JObject.Parse(downloadTokenJson)?["csrf"]?["value"].ToString();


            //get the download url
            var downloadLocationQueryUrl = serverUrl.UrlCombine($"resources/v1/modeler/documents/{DocumentObjectId}/files/{ObjectId}/DownloadTicket");
            request = new HttpRequestMessage()
            {
                RequestUri = new Uri(downloadLocationQueryUrl),
                Method = HttpMethod.Put
            };
            request.Headers.Add("ENO_CSRF_TOKEN", downloadToken);

            client = new HttpClient();
            client.DefaultRequestHeaders.Add("Cookie", cookies);

            var downloadLocationQueryJson = client.SendAsync(request).Result.Content.ReadAsStringAsync().Result;
            var datalements = JObject.Parse(downloadLocationQueryJson)["data"].First()["dataelements"];

            MemoryStream result;
            if (datalements == null)
            {
                result = new MemoryStream();
            }
            else
            {
                var downloadUrl = datalements["ticketURL"].ToString();

                //download the file
                client = new HttpClient();
                var response = client.GetAsync(downloadUrl).Result;
                result = new MemoryStream();
                response.Content.CopyTo(result, null, CancellationToken.None);
            }

            return result;
        }
    }
}
