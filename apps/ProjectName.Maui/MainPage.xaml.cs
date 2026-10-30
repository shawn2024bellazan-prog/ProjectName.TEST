namespace ProjectName.Maui;

public partial class MainPage : ContentPage
{
    public MainPage() => InitializeComponent();

    private void OnSubmitClicked(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PromptInput.Text))
        {
            Response.Text = "Enter a request before submitting.";
            return;
        }
        Response.Text = "HALT — NO_EFFECTIVE_GRANT. Your request was not executed.";
        SemanticScreenReader.Announce(Response.Text);
    }
}
