// ReRT - https://github.com/vengjean/rert-esapi
// Copyright (c) 2025-2026 Veng Jean Heng. Licensed under the MIT License; see LICENSE.
// SPDX-License-Identifier: MIT

// Renders a DVH plot to a PNG (System.Drawing) and base64-encodes it as a
// data: URI suitable for an inline <img src="..."/> tag in the HTML body.
// Emitting base64 (rather than a temp file passed to Spire as a Picture) keeps
// the image part of the HTML body that Spire's AppendHTML consumes.
//
// Note: ESAPI-coupled (consumes DVHData) and System.Drawing-coupled (uses
// GDI+); not unit-tested. Validated by the Eclipse smoke test.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using VMS.TPS.Common.Model.API;
using VMS.TPS.Common.Model.Types;

namespace ReRT.Reporting
{
    public sealed class DvhPlotter
    {
        /// <summary>
        /// Render the DVH curves and return a base64 data URI ready to drop
        /// into an <c>&lt;img src="..."&gt;</c> tag. Returns an empty string
        /// when the curves list is null/empty so the caller can splice it
        /// directly into a token.
        /// </summary>
        public string RenderToDataUri(IReadOnlyList<DVHData> curves, IReadOnlyList<string> structureNames, string title)
        {
            if (curves == null || curves.Count == 0) return string.Empty;

            const int width = 900;
            const int height = 600;
            const int marginLeft = 60, marginRight = 40, marginTop = 40, marginBottom = 60;

            using (var bmp = new Bitmap(width, height))
            using (var g = Graphics.FromImage(bmp))
            using (var axisPen = new Pen(Color.Black, 1))
            using (var font = new Font("Arial", 10))
            {
                g.Clear(Color.White);
                int plotW = width - marginLeft - marginRight;
                int plotH = height - marginTop - marginBottom;

                g.DrawLine(axisPen, marginLeft, height - marginBottom, width - marginRight, height - marginBottom);
                g.DrawLine(axisPen, marginLeft, height - marginBottom, marginLeft, marginTop);
                g.DrawString(title, font, Brushes.Black, marginLeft + plotW / 2 - 60, marginTop - 30);
                g.DrawString("Dose (cGy)", font, Brushes.Black, width - marginRight - 80, height - marginBottom + 25);
                g.DrawString("Volume (%)", font, Brushes.Black, marginLeft - 30, marginTop - 30);

                double maxDose = 0;
                foreach (var dvh in curves)
                {
                    if (dvh.CurveData != null && dvh.CurveData.Any())
                    {
                        double m = dvh.MaxDose.Dose;
                        if (m > maxDose) maxDose = m;
                    }
                }

                using (var gridPen = new Pen(Color.FromArgb((int)(0.3 * 255), 0, 0, 0), 1))
                {
                    gridPen.DashStyle = DashStyle.Dot;
                    for (int i = 0; i <= 10; i++)
                    {
                        int x = marginLeft + i * plotW / 10;
                        g.DrawLine(gridPen, x, height - marginBottom, x, marginTop);
                        string xLabel = (i * maxDose / 10).ToString("F0");
                        var labelSize = g.MeasureString(xLabel, font);
                        g.DrawString(xLabel, font, Brushes.Black, x - labelSize.Width / 2, height - marginBottom + 5);
                    }
                    for (int i = 0; i <= 10; i++)
                    {
                        int y = height - marginBottom - i * plotH / 10;
                        g.DrawLine(gridPen, marginLeft, y, width - marginRight, y);
                        g.DrawString((i * 10).ToString(), font, Brushes.Black, marginLeft - 40, y - 8);
                    }
                }

                Color[] palette =
                {
                    Color.Blue, Color.Red, Color.Green, Color.Orange, Color.Purple,
                    Color.Brown, Color.Teal, Color.Magenta, Color.DarkCyan, Color.DarkGoldenrod,
                };
                var legendItems = new List<Tuple<string, Color>>();

                for (int s = 0; s < curves.Count; s++)
                {
                    var dvh = curves[s];
                    var color = palette[s % palette.Length];
                    using (var structurePen = new Pen(color, 2))
                    {
                        PointF? prev = null;
                        if (dvh.CurveData != null)
                        {
                            for (int i = 0; i < dvh.CurveData.Count(); i++)
                            {
                                var p = dvh.CurveData[i];
                                double x = marginLeft + (p.DoseValue.Dose / maxDose) * plotW;
                                double y = height - marginBottom - (p.Volume / 100.0) * plotH;
                                var pt = new PointF((float)x, (float)y);
                                using (var brush = new SolidBrush(color))
                                    g.FillEllipse(brush, pt.X - 1, pt.Y - 1, 2, 2);
                                if (prev.HasValue) g.DrawLine(structurePen, prev.Value, pt);
                                prev = pt;
                            }
                        }
                    }
                    string legendName = (structureNames != null && structureNames.Count > s) ? structureNames[s] : $"Structure {s + 1}";
                    legendItems.Add(Tuple.Create(legendName, color));
                }

                int legendX = width - marginRight - 140;
                int legendY = marginTop + 10;
                const int legendBox = 16;
                const int legendSpacing = 22;
                for (int i = 0; i < legendItems.Count; i++)
                {
                    var item = legendItems[i];
                    using (var brush = new SolidBrush(item.Item2))
                        g.FillRectangle(brush, legendX, legendY + i * legendSpacing, legendBox, legendBox);
                    g.DrawString(item.Item1, font, Brushes.Black, legendX + legendBox + 8, legendY + i * legendSpacing + 1);
                }

                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                }
            }
        }
    }
}
