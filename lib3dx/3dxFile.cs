﻿using libCommon;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace lib3dx
{
    public class _3dxFile : _3dxItem
    {
        readonly string DocumentObjectId;
        public string FileRevision;
        public ulong Size;

        public _3dxFile(string objectId, string name, _3dxItem? parent, DateTime creationTimeUtc, DateTime lastWriteTimeUtc, DateTime lastAccessTimeUtc, string documentObjectId, string fileRevision, ulong size) : base(objectId, name, parent, creationTimeUtc, lastWriteTimeUtc, lastAccessTimeUtc)
        {
            DocumentObjectId = documentObjectId;
            FileRevision = fileRevision;
            Size = size;
        }

        public Stream Download(string serverUrl, _3dxCookies cookies)
        {
            try
            {
                Log.WriteLine("Downloading file");

                //get download token
                var objectUrl = serverUrl.UrlCombine(@$"resources/v1/application/CSRF");

                var request = new HttpRequestMessage()
                {
                    RequestUri = new Uri(objectUrl),
                    Method = HttpMethod.Get
                };

                var httpClient = new HttpClient();
                request.Headers.Add("Cookie", cookies._3DSpace.Cookie);

                var downloadTokenJson = httpClient.SendAsync(request).Result.Content.ReadAsStringAsync().Result;
                var downloadToken = (JObject.Parse(downloadTokenJson)?["csrf"]?["value"]?.ToString()) ?? throw new Exception($"Could not get Download Token for file with id {DocumentObjectId}. {FullPath}");


                //get the download url
                var downloadLocationQueryUrl = serverUrl.UrlCombine($"resources/v1/modeler/documents/{DocumentObjectId}/files/{ObjectId}/DownloadTicket");
                request = new HttpRequestMessage()
                {
                    RequestUri = new Uri(downloadLocationQueryUrl),
                    Method = HttpMethod.Put
                };
                request.Headers.Add("ENO_CSRF_TOKEN", downloadToken);

                httpClient = new HttpClient();
                request.Headers.Add("Cookie", cookies._3DSpace.Cookie);

                var downloadLocationQueryJson = httpClient.SendAsync(request).Result.Content.ReadAsStringAsync().Result;
                var datalements = JObject.Parse(downloadLocationQueryJson)["data"]?.First()["dataelements"];

                Stream result;
                if (datalements == null)
                {
                    result = Stream.Null;
                }
                else
                {
                    var downloadUrl = datalements["ticketURL"]?.ToString();

                    if (downloadToken == null)
                    {
                        throw new Exception($"Could not get Download URL for file with id {DocumentObjectId}. {FullPath}");
                    }

                    //download the file
                    httpClient = libCommon.Utilities.WebUtility.NewHttpClientWithCompression();

                    //settings to allow large files to be downloaded
                    httpClient.Timeout = TimeSpan.FromMinutes(30);  //default is 1 minute, 40 seconds
                    var opt = HttpCompletionOption.ResponseHeadersRead; //to avoid: Cannot write more bytes to the buffer than the configured maximum buffer size: 2147483647.

                    var response = httpClient.GetAsync(downloadUrl, opt).Result;

                    //todo: Consider returning response.Content.ReadAsStream(), instead of MemoryStream being the intermediary. Might need to be seekable though;
                    //result = new MemoryStream();
                    //response.Content.CopyTo(result, null, CancellationToken.None);

                    result = response.Content.ReadAsStream();
                }

                return result;
            }
            catch (Exception ex)
            {
                Log.WriteLine($"Error while downloading file to MemoryStream:{Environment.NewLine}{ex}");
            }

            return (MemoryStream)Stream.Null;
        }
    }
}
