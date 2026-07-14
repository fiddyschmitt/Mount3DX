using System;

namespace lib3dxVFS.WebDAV.Stores
{
    static class WebDavDates
    {
        //The Windows WebDAV redirector converts creationdate/getlastmodified from a PROPFIND response
        //into a Win32 FILETIME, which cannot represent any moment before 1601-01-01. A date earlier
        //than that - most commonly DateTime.MinValue (year 0001), produced when a 3DX "originated" or
        //"modified" field is missing or fails to parse - serializes to a syntactically valid date
        //string, but the redirector's FILETIME conversion of it fails with ERROR_INVALID_PARAMETER and
        //it rejects the whole response ("The parameter is incorrect"), so the folder won't open.
        //Clamp anything out of range to a safe, in-range value before serving it.
        static readonly DateTime MinServable = new(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        public static DateTime Clamp(DateTime value)
        {
            return value < MinServable ? MinServable : value;
        }
    }
}
