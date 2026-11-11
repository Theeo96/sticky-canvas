using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace StickyCanvas
{
    public static class TextCodec
    {
        public static FlowDocument Document(Element e)
        {
            var first = e.Paragraphs.SelectMany(p => p.Runs).FirstOrDefault() ?? new TextRun();
            var doc = new FlowDocument { PagePadding = new Thickness(0), FontFamily = new FontFamily(first.Font), FontSize = first.Size, Foreground = UI.Brush(first.Color) };
            foreach (var p in e.Paragraphs)
            {
                var paragraph = new Paragraph { Margin = new Thickness(0), TextAlignment = (TextAlignment)Enum.Parse(typeof(TextAlignment), p.Align) };
                if (p.LineHeight > 0) paragraph.LineHeight = p.LineHeight;
                foreach (var r in p.Runs) paragraph.Inlines.Add(Run(r));
                doc.Blocks.Add(paragraph);
            }
            if (doc.Blocks.Count == 0) doc.Blocks.Add(new Paragraph(new Run("")) { Margin = new Thickness(0) });
            return doc;
        }
        public static Run Run(TextRun r)
        {
            return new Run(r.Text) { FontFamily = new FontFamily(r.Font), FontSize = r.Size, Foreground = UI.Brush(r.Color), FontWeight = r.Bold ? FontWeights.Bold : FontWeights.Normal, FontStyle = r.Italic ? FontStyles.Italic : FontStyles.Normal, TextDecorations = r.Underline ? TextDecorations.Underline : null };
        }
        static void CaptureInlines(InlineCollection inlines, List<TextRun> runs)
        {
            foreach (var inline in inlines)
            {
                var run = inline as Run;
                if (run != null) runs.Add(new TextRun { Text = run.Text, Font = run.FontFamily.Source, Size = run.FontSize, Color = Color(run.Foreground), Bold = run.FontWeight.ToOpenTypeWeight() >= 600, Italic = run.FontStyle == FontStyles.Italic, Underline = run.TextDecorations != null && run.TextDecorations.Any(d => d.Location == TextDecorationLocation.Underline) });
                else if (inline is LineBreak) runs.Add(new TextRun { Text = "\n", Font = inline.FontFamily.Source, Size = inline.FontSize, Color = Color(inline.Foreground) });
                else if (inline is Span) CaptureInlines(((Span)inline).Inlines, runs);
            }
        }
        public static List<TextParagraph> Capture(FlowDocument doc)
        {
            var result = new List<TextParagraph>();
            foreach (var block in doc.Blocks)
            {
                var p = block as Paragraph;
                if (p == null) throw new InvalidOperationException("현재 문단·글자 서식만 지원합니다. 복잡한 표/목록은 일반 텍스트로 붙여넣으세요.");
                var item = new TextParagraph { Align = p.TextAlignment == TextAlignment.Center ? "Center" : p.TextAlignment == TextAlignment.Right ? "Right" : "Left", LineHeight = double.IsNaN(p.LineHeight) ? 0 : p.LineHeight };
                CaptureInlines(p.Inlines, item.Runs);
                if (item.Runs.Count == 0) item.Runs.Add(new TextRun { Text = "", Font = p.FontFamily.Source, Size = p.FontSize, Color = Color(p.Foreground) });
                result.Add(item);
            }
            if (result.Count == 0) result.Add(new TextParagraph { Runs = new List<TextRun> { new TextRun() } });
            return result;
        }
        static string Color(Brush b) { var solid = b as SolidColorBrush; return solid == null ? "#FF29251F" : solid.Color.ToString(); }
        public static FrameworkElement Display(Element e)
        {
            var stack = new StackPanel { IsHitTestVisible = false };
            foreach (var p in e.Paragraphs)
            {
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap, TextAlignment = (TextAlignment)Enum.Parse(typeof(TextAlignment), p.Align) };
                if (p.LineHeight > 0) { text.LineHeight = p.LineHeight; text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight; }
                var first = p.Runs.FirstOrDefault() ?? new TextRun(); text.FontFamily = new FontFamily(first.Font); text.FontSize = first.Size;
                text.MinHeight = p.LineHeight > 0 ? p.LineHeight : first.Size * 1.2;
                foreach (var r in p.Runs) text.Inlines.Add(Run(r));
                stack.Children.Add(text);
            }
            return new Border { Child = stack, Padding = new Thickness(e.Padding), IsHitTestVisible = false };
        }
    }
}
