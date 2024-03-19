﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace libCommon.Utilities
{
    public static class FileUtility
    {
        public static string GetAvailableDriveLetter(bool alphabetical = false)
        {
            var existingLetters = Directory.GetLogicalDrives();

            var candidates = Enumerable.Range('A', 26);
            if (!alphabetical) candidates = candidates.Reverse();

            var availableLetter = candidates
                                    .Select(i => @$"{(char)i}:\")
                                    .FirstOrDefault(candidate => !existingLetters.Any(existing => candidate.Equals(existing, StringComparison.CurrentCultureIgnoreCase)));

            if (availableLetter == null)
            {
                throw new Exception($"Could not find an available drive letter.");
            }

            return availableLetter;
        }

        public static string ReplaceInvalidChars(string filename)
        {
            return string.Join("_", filename.Split(Path.GetInvalidFileNameChars()));
        }

        public static string WebdavCompatibleFilename(string filename)
        {
            var derivedName = filename;
            derivedName = ReplaceInvalidChars(derivedName);
            derivedName = derivedName.TrimEnd('.').Trim();
            derivedName = derivedName[..Math.Min(120, derivedName.Length)];
            derivedName = derivedName.TrimEnd('.').Trim();
            return derivedName;
        }
    }
}
