using Newtonsoft.Json;

namespace libCommon
{
    public static class Extensions
    {
        public static string ToString(this IEnumerable<string> values, string separator)
        {
            var result = string.Join(separator, values);
            return result;
        }

        public static string Truncate(this string value, int maxLength, string truncationSuffix = "")
        {
            if (value.Length > maxLength)
            {
                var result = value[..maxLength].Trim() + truncationSuffix;
                return result;
            }
            else
            {
                return value;
            }
        }

        public static string TruncateFilename(this string? value, int maxLength)
        {
            var extension = Path.GetExtension(value) ?? "";
            var truncatedFilename = Path.GetFileNameWithoutExtension(value)?.Truncate(maxLength - extension.Length).Trim() + extension;

            return truncatedFilename;
        }

        public static IEnumerable<T> Recurse<T>(this T source, Func<T, T?> childSelector, bool depthFirst = false)
        {
            var list = new List<T>() { source };
            var childListSelector = new Func<T, IEnumerable<T>>(item =>
            {
                var child = childSelector(item);
                if (child == null)
                {
                    return new List<T>();
                }
                else
                {
                    return new List<T>() { child };
                }
            });

            foreach (var result in Recurse(list, childListSelector, depthFirst))
            {
                yield return result;
            }
        }

        public static IEnumerable<T> Recurse<T>(this IEnumerable<T> source, Func<T, IEnumerable<T>> childSelector, bool depthFirst = false)
        {
            List<T> queue = new(source);

            while (queue.Count > 0)
            {
                var item = queue[0];
                queue.RemoveAt(0);

                var children = childSelector(item);

                if (depthFirst)
                {
                    queue.InsertRange(0, children);
                }
                else
                {
                    queue.AddRange(children);
                }

                yield return item;
            }
        }

        public static T? DeserializeJson<T>(this string json) where T : class
        {
            //var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.Auto };
            var result = JsonConvert.DeserializeObject<T>(json);
            return result;
        }

        public static string SerializeToJson(this object obj)
        {
            //var settings = new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.All };

            string result = JsonConvert.SerializeObject(obj, Newtonsoft.Json.Formatting.Indented);
            return result;
        }

        public static string UrlCombine(this string uri1, string uri2)
        {
            uri1 = uri1.TrimEnd('/');
            uri2 = uri2.TrimStart('/');
            return string.Format("{0}/{1}", uri1, uri2);
        }

    }
}