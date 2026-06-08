using System;
using System.Collections.Generic;
using System.IO;
using PdfSharp.Drawing;
using PdfSharp.Drawing.Layout;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Snippets.Font;
using QRCoder;

namespace BTCPayServer.Ndeet.Plugins.Tipcards;

public static class TipcardPdfGenerator
{
    private static readonly XPoint[] LightningIconPoints =
    [
        new(17.57, 10.7),
        new(17.07, 10.36),
        new(12.77, 10.36),
        new(13.27, 6.6),
        new(12.94, 6.05),
        new(12.28, 6.22),
        new(6.83, 12.76),
        new(6.78, 13.36),
        new(7.27, 13.64),
        new(11.57, 13.64),
        new(11.08, 17.4),
        new(11.41, 17.95),
        new(12.07, 17.78),
        new(17.52, 11.24)
    ];

    static TipcardPdfGenerator()
    {
        GlobalFontSettings.FontResolver ??= new FailsafeFontResolver();
    }

    public static byte[] Generate(TipcardPdfRequest request)
    {
        using var document = new PdfDocument();
        document.Info.Title = $"Tipcards - {request.SetName}";

        double pageW = Mm(request.PageWidthMm);
        double pageH = Mm(request.PageHeightMm);
        double margin = Mm(10);
        int cols = request.Columns;

        double pw = pageW - 2 * margin;
        double ph = pageH - 2 * margin;
        double cardW = pw / cols;

        double padding = Mm(2);
        double innerW = cardW - 2 * padding;
        double qrSize = innerW * 0.48;
        double minCardH = qrSize + Mm(7) + 2 * padding;

        int rowsPerPage = Math.Max(1, (int)(ph / minCardH));
        double cardH = ph / rowsPerPage;
        int cardsPerPage = rowsPerPage * cols;

        using var qrGen = new QRCodeGenerator();

        int totalPages = (int)Math.Ceiling((double)request.Cards.Count / cardsPerPage);
        if (totalPages == 0) totalPages = 1;

        for (int page = 0; page < totalPages; page++)
        {
            var pdfPage = document.AddPage();
            pdfPage.Width = new XUnit(pageW, XGraphicsUnit.Point);
            pdfPage.Height = new XUnit(pageH, XGraphicsUnit.Point);

            using var gfx = XGraphics.FromPdfPage(pdfPage);

            int startIdx = page * cardsPerPage;
            int endIdx = Math.Min(startIdx + cardsPerPage, request.Cards.Count);

            for (int i = startIdx; i < endIdx; i++)
            {
                int pageIdx = i - startIdx;
                int col = pageIdx % cols;
                int row = pageIdx / cols;

                double x = margin + col * cardW;
                double y = margin + row * cardH;

                DrawCard(gfx, request, request.Cards[i], x, y, cardW, cardH, qrGen);
            }

            if (request.CuttingMarkers)
            {
                int rowsOnPage = (int)Math.Ceiling((double)(endIdx - startIdx) / cols);
                DrawMarkers(gfx, margin, cardW, cardH, cols, rowsOnPage);
            }
        }

        using var ms = new MemoryStream();
        document.Save(ms, false);
        return ms.ToArray();
    }

    private static void DrawCard(XGraphics gfx, TipcardPdfRequest req, TipcardPdfItem card,
        double x, double y, double w, double h, QRCodeGenerator qrGen)
    {
        double pad = Mm(2);
        double gap = Mm(2);
        double ix = x + pad, iy = y + pad;
        double iw = w - 2 * pad, ih = h - 2 * pad;

        double qrColW = iw * 0.48;
        double qrSize = qrColW;
        double tcX = ix + qrColW + gap;
        double tcW = iw - qrColW - gap;

        // QR code (BMP format - PNG unsupported on Linux in PDFsharp)
        var qrData = qrGen.CreateQrCode(card.ClaimUrl, QRCodeGenerator.ECCLevel.M);
        var qrCode = new BitmapByteQRCode(qrData);
        byte[] qrBmp = qrCode.GetGraphic(20);

        using var qrStream = new MemoryStream(qrBmp);
        using var qrImage = XImage.FromStream(qrStream);
        gfx.DrawImage(qrImage, ix, iy, qrSize, qrSize);

        // QR logo overlay
        if (req.QrLogo != QrLogoType.None)
        {
            double oSize = qrSize * 0.18;
            double cx = ix + qrSize / 2;
            double cy = iy + qrSize / 2;
            double oR = oSize / 2;

            var bgColor = req.QrLogo == QrLogoType.Bitcoin
                ? XColor.FromArgb(0xF7, 0x93, 0x1A)
                : XColor.FromArgb(0xF7, 0x93, 0x1A);

            gfx.DrawEllipse(XBrushes.White, cx - oR - 1, cy - oR - 1, oSize + 2, oSize + 2);
            gfx.DrawEllipse(new XSolidBrush(bgColor), cx - oR, cy - oR, oSize, oSize);

            if (req.QrLogo == QrLogoType.Bitcoin)
            {
                var logoFont = new XFont("Helvetica", oSize * 0.55, XFontStyleEx.Bold);
                var logoRect = new XRect(cx - oR, cy - oR, oSize, oSize);
                gfx.DrawString("B", logoFont, XBrushes.White, logoRect, XStringFormats.Center);
            }
            else
            {
                DrawLightningIcon(gfx, cx - oR, cy - oR, oSize);
            }
        }

        // Sats below QR
        var satsFont = new XFont("Helvetica", Mm(3.5), XFontStyleEx.Bold);
        var satsRect = new XRect(ix, iy + qrSize + Mm(1), qrSize, Mm(5));
        gfx.DrawString($"{card.Sats:N0} sats", satsFont, XBrushes.Black, satsRect, XStringFormats.TopCenter);

        // Right column: headline, card text, store name
        double ty = iy;

        if (!string.IsNullOrEmpty(req.CardHeadline))
        {
            var hFont = new XFont("Helvetica", Mm(3.2), XFontStyleEx.Bold);
            var tf = new XTextFormatter(gfx);
            tf.DrawString(req.CardHeadline, hFont, XBrushes.Black, new XRect(tcX, ty, tcW, ih));
            ty += EstimateTextHeight(gfx, req.CardHeadline, hFont, tcW) + Mm(1.5);
        }

        if (!string.IsNullOrEmpty(req.CardText))
        {
            var tFont = new XFont("Helvetica", Mm(2.6), XFontStyleEx.Regular);
            double maxH = iy + ih - ty - Mm(4);
            if (maxH > 0)
            {
                var tf = new XTextFormatter(gfx);
                tf.DrawString(req.CardText, tFont, new XSolidBrush(XColor.FromArgb(85, 85, 85)),
                    new XRect(tcX, ty, tcW, maxH));
                ty += EstimateTextHeight(gfx, req.CardText, tFont, tcW) + Mm(1);
            }
        }

        if (!string.IsNullOrEmpty(req.StoreName))
        {
            var sFont = new XFont("Helvetica", Mm(2.2), XFontStyleEx.Regular);
            double storeY = Math.Max(ty + sFont.Height, iy + ih - Mm(1));
            gfx.DrawString(req.StoreName, sFont, new XSolidBrush(XColor.FromArgb(153, 153, 153)),
                new XPoint(tcX, storeY));
        }
    }

    private static void DrawLightningIcon(XGraphics gfx, double x, double y, double size)
    {
        var points = new XPoint[LightningIconPoints.Length];
        for (var i = 0; i < LightningIconPoints.Length; i++)
        {
            points[i] = new XPoint(
                x + LightningIconPoints[i].X / 24.0 * size,
                y + LightningIconPoints[i].Y / 24.0 * size);
        }

        var path = new XGraphicsPath();
        path.AddPolygon(points);
        gfx.DrawPath(XBrushes.White, path);
    }

    private static double EstimateTextHeight(XGraphics gfx, string text, XFont font, double maxWidth)
    {
        var words = text.Split(' ');
        int lines = 1;
        double currentWidth = 0;
        double spaceWidth = gfx.MeasureString(" ", font).Width;

        foreach (var word in words)
        {
            double wordWidth = gfx.MeasureString(word, font).Width;
            if (currentWidth + wordWidth > maxWidth && currentWidth > 0)
            {
                lines++;
                currentWidth = wordWidth + spaceWidth;
            }
            else
            {
                currentWidth += wordWidth + spaceWidth;
            }
        }

        return lines * font.Height * 1.15;
    }

    private static void DrawMarkers(XGraphics gfx, double margin, double cardW, double cardH,
        int cols, int rows)
    {
        var pen = new XPen(XColor.FromArgb(136, 136, 136), 0.5);

        for (int r = 0; r <= rows; r++)
        {
            double y = margin + r * cardH;
            gfx.DrawLine(pen, margin, y, margin + cols * cardW, y);
        }

        for (int c = 0; c <= cols; c++)
        {
            double x = margin + c * cardW;
            gfx.DrawLine(pen, x, margin, x, margin + rows * cardH);
        }
    }

    private static double Mm(double mm) => mm * 72.0 / 25.4;
}

public class TipcardPdfRequest
{
    public double PageWidthMm { get; set; } = 210;
    public double PageHeightMm { get; set; } = 297;
    public int Columns { get; set; } = 3;
    public bool CuttingMarkers { get; set; } = true;
    public string SetName { get; set; }
    public string CardHeadline { get; set; }
    public string CardText { get; set; }
    public string StoreName { get; set; }
    public QrLogoType QrLogo { get; set; }
    public List<TipcardPdfItem> Cards { get; set; } = new();
}

public class TipcardPdfItem
{
    public string ClaimUrl { get; set; }
    public long Sats { get; set; }
}
