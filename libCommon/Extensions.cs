using Newtonsoft.Json;
using System.Globalization;

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

        public static string Pluralize(this string singular, int count)
        {
            if (count == 1)
            {
                return singular;
            }

            return $"{singular}s";
        }

        public static string TruncateFilename(this string? value, int maxLength)
        {
            value ??= "";

            var extension = Path.GetExtension(value);

            //an "extension" that would swallow most of the budget isn't really one (e.g. a name
            //that merely contains a dot); truncate the whole name instead
            if (extension.Length > maxLength / 2)
            {
                extension = "";
            }

            var stem = extension.Length > 0 ? Path.GetFileNameWithoutExtension(value) : value;
            var truncatedFilename = stem.Truncate(maxLength - extension.Length).Trim() + extension;

            return truncatedFilename;
        }

        public static string FormatTimeSpan(this TimeSpan timeSpan)
        {
            static string? FormatPart(int quantity, string name) => quantity > 0 ? $"{quantity} {name}{(quantity > 1 ? "s" : "")}" : null;
            return string.Join(", ", new[] { 
                FormatPart(timeSpan.Days, "day"), 
                FormatPart(timeSpan.Hours, "hour"), 
                FormatPart(timeSpan.Minutes, "minute"),
                FormatPart(timeSpan.Seconds, "second")}
            .Where(x => x != null)
            .Take(2));
        }

        public static IEnumerable<T> Recurse<T>(this T source, Func<T, T?> childSelector, bool depthFirst = false)
        {
            var list = new List<T>() { source };
            var childListSelector = new Func<T, IEnumerable<T>>(item =>
            {
                var child = childSelector(item);
                if (child == null)
                {
                    return [];
                }
                else
                {
                    return [child];
                }
            });

            foreach (var result in Recurse(list, childListSelector, depthFirst))
            {
                yield return result;
            }
        }

        public static IEnumerable<T> Recurse<T>(this IEnumerable<T> source, Func<T, IEnumerable<T>> childSelector, bool depthFirst = false)
        {
            //This walks the whole document tree several times per refresh. A List used as a queue
            //shifts every remaining element on each RemoveAt(0), which made it O(n²); use real
            //queue/stack structures instead.
            if (depthFirst)
            {
                //pre-order: push children in reverse so the first child is visited first
                var stack = new Stack<T>(source.Reverse());

                while (stack.Count > 0)
                {
                    var item = stack.Pop();

                    foreach (var child in childSelector(item).Reverse())
                    {
                        stack.Push(child);
                    }

                    yield return item;
                }
            }
            else
            {
                var queue = new Queue<T>(source);

                while (queue.Count > 0)
                {
                    var item = queue.Dequeue();

                    foreach (var child in childSelector(item))
                    {
                        queue.Enqueue(child);
                    }

                    yield return item;
                }
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