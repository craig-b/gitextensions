using GitCommands;
using GitExtensions.Extensibility.Git;
using GitUI.CommandsDialogs;
using NSubstitute;

namespace GitCommandsTests.Editing;

/// <summary>
///  Tests for <see cref="FormSparseWorkingCopyViewModel"/>.
/// </summary>
public class FormSparseWorkingCopyViewModelTests
{
    private string _tempDir = null!;
    private string _infoDir = null!;
    private IGitModule _module = null!;
    private IGitUICommands _gitUICommands = null!;
    private int _refreshCount;

    [SetUp]
    public void Setup()
    {
        _tempDir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "FormSparseWorkingCopyViewModelTests_" + Guid.NewGuid());
        _infoDir = Path.Combine(_tempDir, "info");
        Directory.CreateDirectory(_infoDir);

        _module = Substitute.For<IGitModule>();
        _module.ResolveGitInternalPath("info").Returns(_infoDir);

        _gitUICommands = Substitute.For<IGitUICommands>();
        _gitUICommands.Module.Returns(_module);

        _refreshCount = 0;
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private FormSparseWorkingCopyViewModel CreateModel(string? effectiveSetting)
    {
        _module.GetEffectiveSetting(FormSparseWorkingCopyViewModel.SettingCoreSparseCheckout).Returns(effectiveSetting);
        return new FormSparseWorkingCopyViewModel(_gitUICommands, () => _refreshCount++);
    }

    [TestCase("true", true)]
    [TestCase("True", true)]
    [TestCase("false", false)]
    [TestCase(null, false)]
    [TestCase("", false)]
    public void Initial_IsSparseCheckoutEnabled_should_reflect_effective_setting(string? settingValue, bool expected)
    {
        FormSparseWorkingCopyViewModel model = CreateModel(settingValue);

        model.IsSparseCheckoutEnabled.Should().Be(expected);
    }

    [Test]
    public void IsWithUnsavedChanges_should_be_false_initially()
    {
        FormSparseWorkingCopyViewModel model = CreateModel("false");

        model.IsWithUnsavedChanges().Should().BeFalse();
    }

    [Test]
    public void IsWithUnsavedChanges_should_be_true_after_toggling_enabled_state()
    {
        FormSparseWorkingCopyViewModel model = CreateModel("false");

        model.IsSparseCheckoutEnabled = true;

        model.IsWithUnsavedChanges().Should().BeTrue();
    }

    [Test]
    public void IsWithUnsavedChanges_should_be_true_when_rules_text_differs_from_on_disk_baseline()
    {
        FormSparseWorkingCopyViewModel model = CreateModel("true");
        model.SetRulesTextAsOnDisk("baseline");

        model.RulesText = "different";

        model.IsWithUnsavedChanges().Should().BeTrue();
    }

    [Test]
    public void IsWithUnsavedChanges_should_be_false_after_rules_text_set_back_to_on_disk_baseline()
    {
        FormSparseWorkingCopyViewModel model = CreateModel("true");
        model.SetRulesTextAsOnDisk("baseline");
        model.RulesText = "different";

        model.RulesText = "baseline";

        model.IsWithUnsavedChanges().Should().BeFalse();
    }

    [Test]
    public void PropertyChanged_should_fire_when_IsSparseCheckoutEnabled_changes()
    {
        FormSparseWorkingCopyViewModel model = CreateModel("false");
        int fireCount = 0;
        model.PropertyChanged += (_, _) => fireCount++;

        model.IsSparseCheckoutEnabled = true;

        fireCount.Should().Be(1);
    }

    [Test]
    public void PropertyChanged_should_not_fire_when_IsSparseCheckoutEnabled_set_to_same_value()
    {
        FormSparseWorkingCopyViewModel model = CreateModel("false");
        int fireCount = 0;
        model.PropertyChanged += (_, _) => fireCount++;

        model.IsSparseCheckoutEnabled = false;

        fireCount.Should().Be(0);
    }

    [Test]
    public void PropertyChanged_should_fire_when_RulesText_changes()
    {
        FormSparseWorkingCopyViewModel model = CreateModel("false");
        int fireCount = 0;
        model.PropertyChanged += (_, _) => fireCount++;

        model.RulesText = "abc";

        fireCount.Should().Be(1);
    }

    [Test]
    public void PropertyChanged_should_not_fire_when_RulesText_set_to_same_value()
    {
        FormSparseWorkingCopyViewModel model = CreateModel("false");
        model.RulesText = "abc";
        int fireCount = 0;
        model.PropertyChanged += (_, _) => fireCount++;

        model.RulesText = "abc";

        fireCount.Should().Be(0);
    }

    [Test]
    public void PropertyChanged_should_fire_for_IsRefreshWorkingCopyOnSave_even_when_set_to_same_value()
    {
        // Unlike IsSparseCheckoutEnabled and RulesText, the IsRefreshWorkingCopyOnSave setter has
        // no equality early-out in the source, so it always raises PropertyChanged.
        FormSparseWorkingCopyViewModel model = CreateModel("false");
        int fireCount = 0;
        model.PropertyChanged += (_, _) => fireCount++;
        bool currentValue = model.IsRefreshWorkingCopyOnSave;

        model.IsRefreshWorkingCopyOnSave = currentValue;

        fireCount.Should().Be(1);
    }

    [Test]
    public void SaveChanges_should_invoke_refresh_delegate_once_when_enabled()
    {
        FormSparseWorkingCopyViewModel model = CreateModel("false");
        model.IsRefreshWorkingCopyOnSave = true;

        model.SaveChanges();

        _refreshCount.Should().Be(1);
    }

    [Test]
    public void SaveChanges_should_not_invoke_refresh_delegate_when_disabled()
    {
        FormSparseWorkingCopyViewModel model = CreateModel("false");
        model.IsRefreshWorkingCopyOnSave = false;

        model.SaveChanges();

        _refreshCount.Should().Be(0);
    }

    [Test]
    public void SaveChanges_should_persist_toggled_enabled_state_as_lowercase_string()
    {
        FormSparseWorkingCopyViewModel model = CreateModel("false");
        model.IsSparseCheckoutEnabled = true;

        model.SaveChanges();

        _module.Received(1).SetSetting(FormSparseWorkingCopyViewModel.SettingCoreSparseCheckout, "true");
    }

    [Test]
    public void SaveChanges_should_not_call_SetSetting_when_enabled_state_unchanged()
    {
        FormSparseWorkingCopyViewModel model = CreateModel("false");

        model.SaveChanges();

        _module.DidNotReceive().SetSetting(Arg.Any<string>(), Arg.Any<string>());
    }

    [Test]
    public void SaveChanges_should_write_rules_text_to_sparse_checkout_file_when_changed()
    {
        FormSparseWorkingCopyViewModel model = CreateModel("true");
        model.SetRulesTextAsOnDisk(string.Empty);
        model.RulesText = "/foo" + Environment.NewLine + "/bar";

        model.SaveChanges();

        string filePath = model.GetPathToSparseCheckoutFile().FullName;
        File.Exists(filePath).Should().BeTrue();
        string content = GitModule.SystemEncoding.GetString(File.ReadAllBytes(filePath));
        content.Should().Be(model.RulesText);
    }

    [Test]
    public void SaveChanges_should_not_rewrite_rules_file_on_second_unchanged_save()
    {
        FormSparseWorkingCopyViewModel model = CreateModel("true");
        model.SetRulesTextAsOnDisk(string.Empty);
        model.RulesText = "/foo";
        model.SaveChanges();
        string filePath = model.GetPathToSparseCheckoutFile().FullName;
        File.Exists(filePath).Should().BeTrue();
        File.Delete(filePath);

        model.SaveChanges();

        File.Exists(filePath).Should().BeFalse();
    }

    [Test]
    public void TurningOff_with_only_pass_filter_rules_should_not_raise_confirmation_and_leave_rules_unchanged()
    {
        FormSparseWorkingCopyViewModel model = CreateModel("true");
        model.IsSparseCheckoutEnabled = false;
        string original = string.Join("\n", "/*", "# a comment", "   ", "/*");
        model.RulesText = original;
        bool eventFired = false;
        model.ComfirmAdjustingRulesOnDeactRequested += (_, _) => eventFired = true;

        model.SaveChanges();

        eventFired.Should().BeFalse();
        model.RulesText.Should().Be(original);
    }

    [Test]
    public void TurningOff_with_nontrivial_rules_should_raise_confirmation_with_nonempty_flag()
    {
        FormSparseWorkingCopyViewModel model = CreateModel("true");
        model.IsSparseCheckoutEnabled = false;
        model.RulesText = "/foo\n/bar";
        FormSparseWorkingCopyViewModel.ComfirmAdjustingRulesOnDeactEventArgs? received = null;
        model.ComfirmAdjustingRulesOnDeactRequested += (_, args) => received = args;

        model.SaveChanges();

        received.Should().NotBeNull();
        received!.IsCurrentRuleSetEmpty.Should().BeFalse();
    }

    [Test]
    public void TurningOff_with_nontrivial_rules_should_leave_rules_unchanged_when_handler_cancels()
    {
        FormSparseWorkingCopyViewModel model = CreateModel("true");
        model.IsSparseCheckoutEnabled = false;
        string original = "/foo\n/bar";
        model.RulesText = original;
        model.ComfirmAdjustingRulesOnDeactRequested += (_, args) => args.Cancel = true;

        model.SaveChanges();

        model.RulesText.Should().Be(original);
    }

    [Test]
    public void TurningOff_with_nontrivial_rules_should_comment_out_original_lines_when_confirmed()
    {
        FormSparseWorkingCopyViewModel model = CreateModel("true");
        model.IsSparseCheckoutEnabled = false;
        model.RulesText = "/foo\n/bar";
        model.ComfirmAdjustingRulesOnDeactRequested += (_, args) => args.Cancel = false;

        model.SaveChanges();

        string expected = string.Join(Environment.NewLine, "/*", "#/foo", "#/bar");
        model.RulesText.Should().Be(expected);
    }

    [Test]
    public void TurningOff_with_only_comments_and_whitespace_should_not_raise_confirmation()
    {
        // BEHAVIOUR DISCREPANCY from the original brief: the brief predicted that rules
        // consisting only of comments/whitespace would raise ComfirmAdjustingRulesOnDeactRequested
        // with IsCurrentRuleSetEmpty == true. Reading SaveChangesTurningOffSparseSpecialCase's
        // actual logic: it filters RulesText down to "rulelines" (non-empty, non-comment lines),
        // then does `if (rulelines.All(l => l == "/*")) return;` before ever raising the event.
        // Enumerable.All on an EMPTY sequence is vacuously true in .NET, so when the text is only
        // comments/blank lines (rulelines.Count == 0), this early-return fires and the event is
        // NEVER raised - the same no-op path as the "only /* lines" case, just reached by a
        // different route. As a corollary, by the time the event-firing code further down runs
        // `new(rulelines.Count == 0)`, rulelines is guaranteed non-empty (otherwise the method
        // would already have returned above), so IsCurrentRuleSetEmpty is in practice always
        // false wherever the event does fire - see
        // TurningOff_with_nontrivial_rules_should_raise_confirmation_with_nonempty_flag above.
        // This test pins the actually-observed behaviour instead of the brief's prediction.
        FormSparseWorkingCopyViewModel model = CreateModel("true");
        model.IsSparseCheckoutEnabled = false;
        string original = string.Join("\n", "# only a comment", "   ", string.Empty);
        model.RulesText = original;
        bool eventFired = false;
        model.ComfirmAdjustingRulesOnDeactRequested += (_, _) => eventFired = true;

        model.SaveChanges();

        eventFired.Should().BeFalse();
        model.RulesText.Should().Be(original);
    }
}
