using libCommon;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace lib3dx.Files
{
    public class _3dxFile : _3dxDownloadableFile
    {
        readonly string DocumentObjectId;

        public _3dxFile(string objectId, string name, _3dxItem? parent, DateTime creationTimeUtc, DateTime lastWriteTimeUtc, DateTime lastAccessTimeUtc, string documentObjectId, ulong size) : base(objectId, name, parent, creationTimeUtc, lastWriteTimeUtc, lastAccessTimeUtc, size)
        {
            DocumentObjectId = documentObjectId;
        }

        public override Stream Download(_3dxServer _3dxServer)
        {
            //When the size is known from the document metadata, present the download as a seekable
            //stream so the WebDAV layer can send Content-Length and honour Range requests without
            //transferring the whole file. When it isn't, stream whatever the server sends.
            if (Size > 0)
            {
                return new RangedDownloadStream(offset => OpenDownloadAsync(_3dxServer, offset), (long)Size, FullPath);
            }

            var response = OpenDownloadAsync(_3dxServer, 0).GetAwaiter().GetResult();
            return response.Content.ReadAsStream();
        }

        //Performs the three-step 3DX download (CSRF token, download ticket, then the file itself)
        //and returns the response with only its headers read, so the body can be streamed.
        async Task<HttpResponseMessage> OpenDownloadAsync(_3dxServer _3dxServer, long offset)
        {
            try
            {
                Log.WriteLine(offset == 0 ? $"Downloading file {FullPath}" : $"Downloading file {FullPath} from byte {offset:N0}");

                //get download token
                var objectUrl = _3dxServer.ServerUrl.UrlCombine(@$"resources/v1/application/CSRF");

                var request = new HttpRequestMessage()
                {
                    RequestUri = new Uri(objectUrl),
                    Method = HttpMethod.Get
                };

                var tokenResponse = await _3dxServer.HttpClient.SendAsync(request).ConfigureAwait(false);
                tokenResponse.EnsureSuccessStatusCode();
                var downloadTokenJson = await tokenResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                var downloadToken = (JObject.Parse(downloadTokenJson)?["csrf"]?["value"]?.ToString()) ?? throw new Exception($"Could not get Download Token for file with id {DocumentObjectId}. {FullPath}");


                //get the download url
                var downloadLocationQueryUrl = _3dxServer.ServerUrl.UrlCombine($"resources/v1/modeler/documents/{DocumentObjectId}/files/{ObjectId}/DownloadTicket");
                request = new HttpRequestMessage()
                {
                    RequestUri = new Uri(downloadLocationQueryUrl),
                    Method = HttpMethod.Put
                };
                request.Headers.Add("ENO_CSRF_TOKEN", downloadToken);

                var ticketResponse = await _3dxServer.HttpClient.SendAsync(request).ConfigureAwait(false);
                ticketResponse.EnsureSuccessStatusCode();
                var downloadLocationQueryJson = await ticketResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                var datalements = JObject.Parse(downloadLocationQueryJson)["data"]?.FirstOrDefault()?["dataelements"];

                if (datalements == null)
                {
                    throw new Exception($"Could not get a download ticket for file with id {ObjectId}. {FullPath}");
                }

                var downloadUrl = datalements["ticketURL"]?.ToString();

                if (downloadUrl == null)
                {
                    throw new Exception($"Could not get Download URL for file with id {DocumentObjectId}. {FullPath}");
                }

                //download the file
                request = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                if (offset > 0)
                {
                    request.Headers.Range = new RangeHeaderValue(offset, null);
                }

                //ResponseHeadersRead so large files are streamed rather than buffered
                //(avoids: Cannot write more bytes to the buffer than the configured maximum buffer size: 2147483647)
                var response = await _3dxServer.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                return response;
            }
            catch (Exception ex)
            {
                //rethrow so the WebDAV layer returns an error, rather than serving an empty file
                Log.WriteLine($"Error while downloading file:{Environment.NewLine}{ex}");
                throw;
            }
        }
    }
}
