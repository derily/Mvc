// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using Microsoft.AspNetCore.Razor.Language;
using Microsoft.Extensions.FileProviders;

namespace Microsoft.AspNetCore.Mvc.Razor.Internal
{
    public class FileProviderRazorProjectItem : RazorProjectItem
    {
        private string _root;
        private string _relativePhysicalPath;

        public FileProviderRazorProjectItem(IFileInfo fileInfo, string basePath, string filePath, string root)
        {
            FileInfo = fileInfo;
            BasePath = basePath;
            FilePath = filePath;
            _root = root;
        }

        public IFileInfo FileInfo { get; }

        public override string BasePath { get; }

        public override string FilePath { get; }

        public override bool Exists => FileInfo.Exists;

        public override string PhysicalPath => FileInfo.PhysicalPath;

        public override string RelativePhysicalPath
        {
            get
            {
                if (_relativePhysicalPath == null)
                {
                    if (Exists)
                    {
                        if (_root != null && !string.IsNullOrEmpty(PhysicalPath) && PhysicalPath.StartsWith(_root))
                        {
                            _relativePhysicalPath = PhysicalPath.Substring(_root.Length + 1); // Include leading separator
                        }
                        else
                        {
                            // Use FilePath if the file is not directly accessible
                            _relativePhysicalPath = NormalizeAndEnsureValidPhysicalPath(FilePath);
                        }
                    }
                }

                return _relativePhysicalPath;
            }
        }

        public override Stream Read()
        {
            return FileInfo.CreateReadStream();
        }

        private static string NormalizeAndEnsureValidPhysicalPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return filePath;
            }

            filePath = filePath.Replace('/', Path.DirectorySeparatorChar);

            if (filePath[0] == Path.DirectorySeparatorChar)
            {
                filePath = filePath.Substring(1);
            }

            return filePath;
        }
    }
}
