using System.Windows;
using System.Windows.Controls;
using FileOrganizer.Core;

namespace FileOrganizer.App;

public partial class RulesWindow : Window
{
    /// <summary>Grid row view-model (DataGrid binds to plain properties).</summary>
    public sealed class RuleRow
    {
        public required OrganizeRule Rule { get; init; }
        public string Name => Rule.Name;
        public string TargetRelativeFolder => Rule.TargetRelativeFolder;
        public int Priority => Rule.Priority;
        public string Summary => string.Join(Rule.Logic == RuleLogic.All ? " AND " : " OR ",
            Rule.Conditions.Select(c => $"{c.Field} {c.Operator} \"{c.Value}\""));
    }

    private AppState State => App.State;

    public RulesWindow()
    {
        InitializeComponent();
        RefreshGrid();
    }

    private void RefreshGrid() =>
        Grid.ItemsSource = State.Config.Rules.OrderBy(r => r.Priority).Select(r => new RuleRow { Rule = r }).ToList();

    private void AddRule_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(RuleNameBox.Text) || string.IsNullOrWhiteSpace(TargetBox.Text))
        {
            Dialogs.Error("Give the rule a name and a target folder (e.g. Jobs\\CV).");
            return;
        }
        var field = TagOf(FieldCombo, RuleField.FileName);
        var op = TagOf(OperatorCombo, RuleOperator.Contains);
        State.Config.Rules.Add(new OrganizeRule
        {
            Name = RuleNameBox.Text.Trim(),
            Priority = (State.Config.Rules.Count + 1) * 10,
            Logic = RuleLogic.All,
            Conditions = { new RuleCondition { Field = field, Operator = op, Value = ValueBox.Text.Trim() } },
            TargetRelativeFolder = TargetBox.Text.Trim(),
        });
        RuleNameBox.Text = ValueBox.Text = TargetBox.Text = "";
        State.Save();
        RefreshGrid();
    }

    private void DeleteRule_Click(object sender, RoutedEventArgs e)
    {
        if (Grid.SelectedItem is RuleRow row && Dialogs.Confirm($"Delete rule \"{row.Name}\"?"))
        {
            State.Config.Rules.Remove(row.Rule);
            State.Save();
            RefreshGrid();
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        State.Save();
        DialogResult = true;
        Close();
    }

    private static T TagOf<T>(ComboBox combo, T fallback) where T : struct, Enum =>
        (combo.SelectedItem as ComboBoxItem)?.Tag is string tag && Enum.TryParse<T>(tag, out var value) ? value : fallback;
}
