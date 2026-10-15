using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;

namespace lib3dx
{
    //Reads the object ids out of a link copied from 3DX: a 3DSpace page such as
    //emxTree.jsp?objectId=..., or a 3DDashboard link, which carries its objects as URL-encoded JSON
    //(...content:X3DContentId={"data":{"items":[{"objectId":...}]}}).
    public static partial class _3dxLink
    {
        //Room for a 3DDashboard link to a handful of objects; longer text isn't a copied link
        public const int MaxLength = 8192;

        //The form the document list uses, e.g. 4698020700001DA85B7CC007000CF4DE
        [GeneratedRegex("^[0-9A-Fa-f]{32}$")]
        private static partial Regex PhysicalId();

        //The form classic 3DSpace pages often use, e.g. 12345.67890.12345.6789
        [GeneratedRegex(@"^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$")]
        private static partial Regex DottedId();

        //objectId as a parameter of the query or the fragment, but not the end of a longer name
        //such as parentObjectId
        [GeneratedRegex(@"(?<=[?&#;/:])objectId=([^&#;/?]*)", RegexOptions.IgnoreCase)]
        private static partial Regex ObjectIdParameter();

        [GeneratedRegex(@"X3DContentId=([^&#]*)", RegexOptions.IgnoreCase)]
        private static partial Regex ContentIdParameter();

        public static bool IsPhysicalId(string id) => PhysicalId().IsMatch(id);

        public static bool IsDottedId(string id) => DottedId().IsMatch(id);

        //The object ids in a copied link, the page's own object first. Empty unless the text is a
        //single http(s) link and nothing else, so copying a message that mentions links does nothing.
        public static IReadOnlyList<string> GetObjectIds(string? text)
        {
            text = text?.Trim();
            if (string.IsNullOrEmpty(text) || text.Length > MaxLength || text.Any(char.IsWhiteSpace))
            {
                return [];
            }

            if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                return [];
            }

            var parameterIds = ObjectIdParameter()
                                .Matches(text)
                                .Select(match => Uri.UnescapeDataString(match.Groups[1].Value));

            var contentIds = ContentIdParameter()
                                .Matches(text)
                                .SelectMany(match => ContentObjectIds(match.Groups[1].Value));

            return parameterIds
                    .Concat(contentIds)
                    .Where(id => IsPhysicalId(id) || IsDottedId(id))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
        }

        //The objectId of each item in an X3DContentId, whose JSON may arrive encoded twice
        static List<string> ContentObjectIds(string value)
        {
            var json = Uri.UnescapeDataString(value);
            if (!json.StartsWith('{'))
            {
                json = Uri.UnescapeDataString(json);
            }

            try
            {
                if (JToken.Parse(json) is not JContainer content)
                {
                    return [];
                }

                return content
                        .Descendants()
                        .OfType<JProperty>()
                        .Where(property => property.Name.Equals("objectId", StringComparison.OrdinalIgnoreCase) && property.Value.Type == JTokenType.String)
                        .Select(property => property.Value.ToString())
                        .ToList();
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }
}
