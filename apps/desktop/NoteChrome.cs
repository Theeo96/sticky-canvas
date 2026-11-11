using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace StickyCanvas
{
    public sealed partial class NoteWindow
    {
        string themedBackground;
        Brush chromeForeground = UI.Ink;
        public Brush ChromeForeground { get { return chromeForeground; } }
        public bool TitleEditing { get { return !title.IsReadOnly; } }
        public static double Luminance(Color c)
        {
            Func<byte, double> linear = delegate(byte b) { double n = b / 255.0; return n <= .04045 ? n / 12.92 : Math.Pow((n + .055) / 1.055, 2.4); };
            return .2126 * linear(c.R) + .7152 * linear(c.G) + .0722 * linear(c.B);
        }
        public static Color ForegroundFor(Color c)
        {
            double alpha = c.A / 255.0;
            c = Color.FromRgb((byte)(c.R * alpha + 255 * (1 - alpha)), (byte)(c.G * alpha + 255 * (1 - alpha)), (byte)(c.B * alpha + 255 * (1 - alpha)));
            double l = Luminance(c);
            return (l + .05) / .05 >= 1.05 / (l + .05) ? Colors.Black : Colors.White;
        }
        void ApplyChromeTheme()
        {
            if (themedBackground != Model.Background)
            {
                themedBackground = Model.Background;
                var bg = UI.Brush(Model.Background);
                chromeForeground = new SolidColorBrush(ForegroundFor(((SolidColorBrush)bg).Color)); chromeForeground.Freeze();
                Resources["ChromeBg"] = bg; Resources["ChromeFg"] = chromeForeground;
                Resources["ChromeLine"] = chromeForeground;
                ThemeTree(chromePanel, bg); ThemeTree(chromeToggle, bg); ThemeTree(saveAlert, bg);
                ((Border)Content).BorderBrush = chromeForeground;
            }
            status.Foreground = chromeForeground;
        }
        void ThemeTree(DependencyObject item, Brush bg)
        {
            var control = item as Control;
            if (control != null)
            {
                control.Background = bg; control.Foreground = chromeForeground; control.BorderBrush = chromeForeground;
                var menuItem = control as MenuItem; if (menuItem != null) menuItem.Template = ChromeMenuTemplate;
                var button = control as Button;
                if (button != null) button.Template = ChromeButtonTemplate;
                var combo = control as ComboBox;
                if (combo != null)
                {
                    combo.Template = ChromeComboTemplate;
                    var style = new Style(typeof(ComboBoxItem));
                    style.Setters.Add(new Setter(Control.BackgroundProperty, bg));
                    style.Setters.Add(new Setter(Control.ForegroundProperty, chromeForeground));
                    foreach (var property in new[] { ComboBoxItem.IsHighlightedProperty, ComboBoxItem.IsSelectedProperty })
                    {
                        var trigger = new Trigger { Property = property, Value = true };
                        trigger.Setters.Add(new Setter(Control.BackgroundProperty, chromeForeground));
                        trigger.Setters.Add(new Setter(Control.ForegroundProperty, bg)); style.Triggers.Add(trigger);
                    }
                    combo.Resources[SystemColors.HighlightBrushKey] = chromeForeground;
                    combo.Resources[SystemColors.HighlightTextBrushKey] = bg;
                    combo.ItemContainerStyle = style;
                }
            }
            var panel = item as Panel; if (panel != null) panel.Background = bg;
            var text = item as TextBlock; if (text != null) text.Foreground = chromeForeground;
            foreach (object child in LogicalTreeHelper.GetChildren(item)) { var d = child as DependencyObject; if (d != null) ThemeTree(d, bg); }
        }
        void ApplyMenuTheme(ContextMenu menu)
        {
            menu.Resources["ChromeBg"] = UI.Brush(Model.Background); menu.Resources["ChromeFg"] = chromeForeground;
            menu.Resources[SystemColors.MenuBrushKey] = UI.Brush(Model.Background);
            menu.Resources[SystemColors.MenuTextBrushKey] = chromeForeground;
            // System highlight uses its own matched foreground/background pair.
            ThemeTree(menu, UI.Brush(Model.Background));
        }
        static readonly ControlTemplate ChromeButtonTemplate = (ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='Button'>
<Border x:Name='surface' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' CornerRadius='2' Padding='{TemplateBinding Padding}'>
<ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border>
<ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='BorderThickness' Value='2'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.5'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        static readonly ControlTemplate ChromeMenuTemplate = (ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='MenuItem'>
<Border x:Name='surface' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding Foreground}' BorderThickness='0' Padding='1'>
<Grid><Grid.ColumnDefinitions><ColumnDefinition Width='22'/><ColumnDefinition Width='*'/><ColumnDefinition Width='18'/></Grid.ColumnDefinitions>
<TextBlock x:Name='check' Visibility='Hidden' Text='✓' Foreground='{TemplateBinding Foreground}' VerticalAlignment='Center'/>
<ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' Margin='3,6'/>
<TextBlock x:Name='arrow' Grid.Column='2' Visibility='Hidden' Text='▸' Foreground='{TemplateBinding Foreground}' VerticalAlignment='Center'/>
<Popup x:Name='PART_Popup' Grid.ColumnSpan='3' IsOpen='{TemplateBinding IsSubmenuOpen}' Placement='Right' AllowsTransparency='True' Focusable='False'>
<Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding Foreground}' BorderThickness='1'><ScrollViewer MaxHeight='500'><ItemsPresenter/></ScrollViewer></Border></Popup>
</Grid></Border><ControlTemplate.Triggers>
<Trigger Property='IsHighlighted' Value='True'><Setter TargetName='surface' Property='BorderThickness' Value='1'/><Setter TargetName='surface' Property='Padding' Value='0'/></Trigger>
<Trigger Property='HasItems' Value='True'><Setter TargetName='arrow' Property='Visibility' Value='Visible'/></Trigger>
<Trigger Property='IsChecked' Value='True'><Setter TargetName='check' Property='Visibility' Value='Visible'/></Trigger>
<Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.5'/></Trigger>
</ControlTemplate.Triggers></ControlTemplate>");
        static readonly ControlTemplate ChromeComboTemplate = (ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='ComboBox'>
<Grid><Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1'/>
<ToggleButton Focusable='False' Background='{TemplateBinding Background}' Foreground='{TemplateBinding Foreground}' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
<ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='{TemplateBinding Background}'><TextBlock Text='▾' Foreground='{TemplateBinding Foreground}' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,5,0'/></Border></ControlTemplate></ToggleButton.Template></ToggleButton>
<ContentPresenter x:Name='selection' Margin='5,3,22,3' IsHitTestVisible='False' Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' VerticalAlignment='Center'/>
<TextBox x:Name='PART_EditableTextBox' Visibility='Collapsed' Margin='3,2,22,2' BorderThickness='0' Background='{TemplateBinding Background}' Foreground='{TemplateBinding Foreground}' VerticalContentAlignment='Center'/>
<Popup x:Name='PART_Popup' IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' AllowsTransparency='True' Focusable='False'>
<Border Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding Foreground}' BorderThickness='1' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='{TemplateBinding MaxDropDownHeight}'><ScrollViewer><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer></Border></Popup>
</Grid><ControlTemplate.Triggers><Trigger Property='IsEditable' Value='True'><Setter TargetName='selection' Property='Visibility' Value='Collapsed'/><Setter TargetName='PART_EditableTextBox' Property='Visibility' Value='Visible'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
        void InitializeHeaderDrag(DockPanel header)
        {
            FinishTitleEdit(); title.ToolTip = "상단바 드래그: 창 이동 · 제목 더블클릭: 편집";
            header.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                var source = e.OriginalSource as DependencyObject;
                while (source != null && source != header)
                {
                    if (source is ButtonBase || (source == title && TitleEditing)) return;
                    source = VisualTreeHelper.GetParent(source);
                }
                if (e.ClickCount == 2) { BeginTitleEdit(); e.Handled = true; return; }
                EndEditing(); e.Handled = true;
                if (Mouse.LeftButton == MouseButtonState.Pressed) DragMove();
            };
            title.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter || e.Key == Key.Escape) { FinishTitleEdit(); Paper.Focus(); e.Handled = true; } };
            title.LostKeyboardFocus += delegate { FinishTitleEdit(); };
        }
        public void BeginTitleEdit() { EndEditing(); title.IsReadOnly = false; title.Cursor = Cursors.IBeam; title.Focus(); title.SelectAll(); }
        public void FinishTitleEdit() { title.IsReadOnly = true; title.Cursor = Cursors.SizeAll; }
    }
}
