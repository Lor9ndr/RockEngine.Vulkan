using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using Orientation = System.Windows.Controls.Orientation;

namespace RockEngine.ShaderSyntax
{
    internal static class QuickInfoContentBuilder
    {
        public static FrameworkElement BuildForFunction(FunctionSignature signature, int overloadCount)
        {
            var panel = new StackPanel { Margin = new Thickness(5)};

            // Signature line (return type and name)
            var signatureText = new TextBlock();
            signatureText.Inlines.Add(new Run(signature.ReturnType + " ") { FontWeight = FontWeights.Normal, Foreground = Brushes.White  });
            signatureText.Inlines.Add(new Run(signature.Name) { FontWeight = FontWeights.Bold, Foreground = Brushes.White });
            signatureText.Inlines.Add(new Run("(") { Foreground = Brushes.White });

            // Parameters
            for (int i = 0; i < signature.Parameters.Count; i++)
            {
                var p = signature.Parameters[i];
                if (i > 0)
                {
                    signatureText.Inlines.Add(new Run(", "));
                }

                // Parameter type and name (optional name)
                signatureText.Inlines.Add(new Run(p.Type) { FontStyle = FontStyles.Italic,  Foreground = Brushes.White });
                if (!string.IsNullOrEmpty(p.Name))
                {
                    signatureText.Inlines.Add(new Run(" " + p.Name) { Foreground = Brushes.White });
                }
            }
            signatureText.Inlines.Add(new Run(")") { Foreground = Brushes.White });
            panel.Children.Add(signatureText);

            // Description (if any)
            if (!string.IsNullOrWhiteSpace(signature.Description))
            {
                var desc = new TextBlock
                {
                    Text = signature.Description,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 5, 0, 5),
                    Foreground = Brushes.White,
                };
                panel.Children.Add(desc);
            }

            // Documentation link
            if (!string.IsNullOrEmpty(signature.DocumentationUrl))
            {
                var linkPanel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
                linkPanel.Children.Add(new TextBlock { Text = "More info: " ,Foreground = Brushes.White });

                var hyperlink = new Hyperlink();
                hyperlink.Inlines.Add(signature.DocumentationUrl);
                hyperlink.NavigateUri = new Uri(signature.DocumentationUrl);
                hyperlink.RequestNavigate += (s, e) =>
                {
                    Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
                    e.Handled = true;
                };

                var linkText = new TextBlock();
                linkText.Inlines.Add(hyperlink);
                linkPanel.Children.Add(linkText);
                panel.Children.Add(linkPanel);
            }

            // Overload indicator
            if (overloadCount > 1)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = $"+ {overloadCount - 1} more overload(s)",
                    FontStyle = FontStyles.Italic,
                    Margin = new Thickness(0, 5, 0, 0),
                    Foreground = Brushes.White
                });
            }

            return panel;
        }
    }
}