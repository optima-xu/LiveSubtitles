using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
namespace LiveSubtitles;
public sealed class SettingsWindow : Window
{
    public SettingsWindow(Settings s)
    {
        Title = "高级设置";
        Width = 650;
        Height = 610;
        MinWidth = 520;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel { Margin = new Thickness(16) };
        Content = root;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var save = new Button { Content = "保存" };
        var cancel = new Button { Content = "取消" };
        buttons.Children.Add(save);
        buttons.Children.Add(cancel);
        cancel.Click += (_, _) => Close();
        var panel = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        TextBox Field(string label, string value, int height = 29)
        {
            panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 9, 0, 4) });
            var box = new TextBox { Text = value, Height = height, AcceptsReturn = height > 35, TextWrapping = height > 35 ? TextWrapping.Wrap : TextWrapping.NoWrap, VerticalScrollBarVisibility = height > 35 ? ScrollBarVisibility.Auto : ScrollBarVisibility.Hidden };
            panel.Children.Add(box);
            return box;
        }
        var ollama = Field("Ollama 地址（识别与翻译共用）", s.OllamaUrl);
        var mt = Field("翻译模型", s.TranslationModel);
        var model = Field("识别模型（更改后请退出并重新启动）", s.AsrModel);
        var runtime = Field("运行环境与模型目录", s.RuntimeDirectory);
        var glossary = Field("术语对照（每行一个，例如：gradient descent = 梯度下降）", s.Glossary, 72);
        ComboBox KeyField(string label, int selected)
        {
            panel.Children.Add(new TextBlock { Text = label + "（Ctrl+Alt+）", Margin = new Thickness(0, 9, 0, 4) });
            var keys = new List<KeyValuePair<int, string>> { new(0x20, "Space / 空格") };
            for (int key = 0x30; key <= 0x39; key++)
                keys.Add(new(key, ((char)key).ToString()));
            for (int key = 0x41; key <= 0x5A; key++)
                keys.Add(new(key, ((char)key).ToString()));
            var box = new ComboBox { ItemsSource = keys, DisplayMemberPath = "Value", SelectedValuePath = "Key", SelectedValue = selected, Height = 29 };
            if (box.SelectedIndex < 0)
                box.SelectedIndex = 0;
            panel.Children.Add(box);
            return box;
        }
        var startKey = KeyField("开始 / 暂停", s.StartHotkey);
        var lockKey = KeyField("锁定 / 解锁字幕", s.LockHotkey);
        var showKey = KeyField("显示 / 隐藏字幕", s.ShowHotkey);
        panel.Children.Add(new TextBlock { Text = "新快捷键在重启后生效。", Margin = new Thickness(0, 4, 0, 0), Foreground = System.Windows.Media.Brushes.DimGray, TextWrapping = TextWrapping.Wrap });
        save.Click += (_, _) =>
        {
            try
            {
                var o = new Uri(ollama.Text.Trim());
                if (o.Scheme != "http" && o.Scheme != "https")
                    throw new Exception("Ollama 地址需要以 http:// 或 https:// 开头。");
                int k0 = (int)startKey.SelectedValue, k1 = (int)lockKey.SelectedValue, k2 = (int)showKey.SelectedValue;
                if (k0 == k1 || k0 == k2 || k1 == k2)
                    throw new Exception("快捷键不能重复。");
                s.OllamaUrl = o.ToString().TrimEnd('/');
                s.TranslationModel = mt.Text.Trim();
                s.AsrModel = model.Text.Trim();
                s.RuntimeDirectory = Path.GetFullPath(runtime.Text.Trim());
                s.Glossary = glossary.Text.Trim();
                s.StartHotkey = k0;
                s.LockHotkey = k1;
                s.ShowHotkey = k2;
                s.Save();
                DialogResult = true;
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "设置未保存"); }
        };
    }
}
