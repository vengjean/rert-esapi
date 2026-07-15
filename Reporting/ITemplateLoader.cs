// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Loads the report HTML template (and any auxiliary text files) from disk.
// Split into an interface so tests can substitute a string template without
// touching the filesystem.
using System.IO;
using System.Reflection;

namespace ReRT.Reporting
{
    public interface ITemplateLoader
    {
        string LoadReportTemplate();
    }

    /// <summary>
    /// Reads <c>Configuration/ReportTemplate.html</c> from the directory next
    /// to the executing assembly (the same convention <see cref="DocxWriter"/>
    /// uses for the institution logo).
    /// </summary>
    public sealed class FileTemplateLoader : ITemplateLoader
    {
        private readonly string _templatePath;

        public FileTemplateLoader()
            : this(ResolveDefaultTemplatePath()) { }

        public FileTemplateLoader(string templatePath)
        {
            _templatePath = templatePath;
        }

        public string LoadReportTemplate()
        {
            if (!File.Exists(_templatePath))
                throw new FileNotFoundException(
                    $"Report template not found at '{_templatePath}'. " +
                    "Ensure Configuration/ReportTemplate.html is copied to the output directory.",
                    _templatePath);
            return File.ReadAllText(_templatePath);
        }

        private static string ResolveDefaultTemplatePath()
            => ResolveTemplatePath("ReportTemplate.html");

        private static string ResolveTemplatePath(string fileName)
        {
            var asmLocation = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(asmLocation))
                asmLocation = System.AppDomain.CurrentDomain.BaseDirectory;
            var dir = Path.GetDirectoryName(asmLocation);
            return Path.Combine(dir, "Configuration", fileName);
        }
    }
}
