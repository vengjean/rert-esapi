// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Isolates Spire.Doc HTML→DOCX conversion — the only file in the repo that
// `using`s Spire.Doc.
//
// In addition to the HTML→DOCX render, this class builds the running page
// header (logo + department name + title and patient identity table) using
// Spire's HeadersFooters API. That header runs on every printed page in Word
// and is a Spire-only feature; expressing it in HTML would not preserve the
// per-page repeat semantics. The logo path and department text come from
// InstitutionConfig.

using System;
using System.Collections.Generic;
using System.IO;
using Spire.Doc;
using Spire.Doc.Documents;
using Spire.Doc.Fields;

namespace ReRT.Reporting
{
    /// <summary>
    /// Page-header data the Word document needs in addition to the HTML body.
    /// Anything that must appear in Word's running header (per-page) lives
    /// here; everything else is in the HTML body.
    /// </summary>
    public sealed class DocxHeader
    {
        public string LogoPath;            // Optional; if file does not exist, header is suppressed.
        public string DepartmentName;      // e.g. "Department of Radiation Oncology" (from InstitutionConfig).
        public string ReportTitle;         // e.g. "Prior RT Physics Consult".
        public string PatientName;         // "Last, First".
        public string PatientId;
        public string PatientDob;
        public string PhysicianName;
    }

    public sealed class DocxWriter
    {
        /// <summary>
        /// Render an HTML body (already token-substituted) into a Word document
        /// with a running page header, then save copies to each output path.
        /// </summary>
        public void WriteHtmlToDocx(string html, DocxHeader header, IReadOnlyList<string> outputPaths)
        {
            if (html == null) throw new ArgumentNullException(nameof(html));
            if (outputPaths == null || outputPaths.Count == 0)
                throw new ArgumentException("At least one output path is required.", nameof(outputPaths));

            using (var document = new Document())
            {
                Section section = document.AddSection();
                section.PageSetup.Margins.All = 72;
                section.PageSetup.PageSize = PageSize.Letter;

                if (header != null)
                {
                    BuildLogoHeader(section, header);
                    BuildPatientHeader(document, section, header);
                    section.HeadersFooters.Header.AddParagraph().AppendText("\n");
                }

                Paragraph bodyPara = section.AddParagraph();
                bodyPara.AppendHTML(html);

                foreach (var outputPath in outputPaths)
                {
                    var dir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);
                    document.SaveToFile(outputPath, FileFormat.Docx);
                }
            }
        }

        private static void BuildLogoHeader(Section section, DocxHeader header)
        {
            if (string.IsNullOrEmpty(header.LogoPath) || !File.Exists(header.LogoPath))
                return;

            Table logoTable = section.HeadersFooters.Header.AddTable(true);
            logoTable.ResetCells(2, 2);

            Paragraph logoPara = logoTable.Rows[0].Cells[0].AddParagraph();
            DocPicture headerPic = logoPara.AppendPicture(System.Drawing.Image.FromFile(header.LogoPath));
            headerPic.Height = 40;
            logoPara.Format.HorizontalAlignment = HorizontalAlignment.Left;

            logoTable.Rows[1].Cells[0].AddParagraph()
                .AppendText(header.DepartmentName ?? string.Empty);

            Paragraph titlePara = logoTable.Rows[0].Cells[1].AddParagraph();
            titlePara.AppendText(header.ReportTitle ?? string.Empty);
            titlePara.Format.HorizontalAlignment = HorizontalAlignment.Right;

            logoTable.ApplyVerticalMerge(0, 1, 1);

            logoTable.TableFormat.Borders.BorderType = BorderStyle.None;
            logoTable.TableFormat.Borders.Color = System.Drawing.Color.White;

            foreach (TableRow row in logoTable.Rows)
            {
                foreach (TableCell cell in row.Cells)
                {
                    cell.CellFormat.Borders.BorderType = BorderStyle.None;
                    cell.CellFormat.Borders.Color = System.Drawing.Color.White;
                }
            }
        }

        private static void BuildPatientHeader(Document document, Section section, DocxHeader header)
        {
            Table headerTable = section.HeadersFooters.Header.AddTable(true);
            headerTable.ResetCells(2, 3);
            headerTable.TableFormat.Borders.BorderType = BorderStyle.Single;
            headerTable.TableFormat.Borders.Color = System.Drawing.Color.Black;

            headerTable.Rows[0].Cells[0].AddParagraph().AppendText(
                $"Patient Name: {header.PatientName}");
            headerTable.Rows[0].Cells[1].AddParagraph().AppendText(
                $"Unique ID: {header.PatientId}");
            headerTable.Rows[0].Cells[2].AddParagraph().AppendText(
                $"DOB: {header.PatientDob}");

            headerTable.Rows[1].Cells[1].AddParagraph().AppendText(
                $"Radiation Oncologist: Dr. {header.PhysicianName}, M.D.");
            headerTable.ApplyHorizontalMerge(1, 0, 2);

            ParagraphStyle style = new ParagraphStyle(document)
            {
                Name = "ReRTReportHeader",
            };
            style.CharacterFormat.FontName = "Times New Roman";
            style.CharacterFormat.FontSize = 10;
            document.Styles.Add(style);

            for (int i = 0; i < headerTable.Rows.Count; i++)
            {
                for (int j = 0; j < headerTable.Rows[i].Cells.Count; j++)
                {
                    TableCell cell = headerTable.Rows[i].Cells[j];
                    cell.CellFormat.Borders.BorderType = BorderStyle.Single;
                    cell.CellFormat.Borders.Color = System.Drawing.Color.Black;
                    foreach (Paragraph para in cell.Paragraphs)
                        para.ApplyStyle(style.Name);
                }
            }
        }
    }
}
