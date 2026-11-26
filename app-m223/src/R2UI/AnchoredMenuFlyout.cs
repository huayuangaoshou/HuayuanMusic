using Avalonia.Controls;

namespace HuayuanPreview.R2;

internal sealed class AnchoredMenuFlyout : MenuFlyout
{
    protected override Control CreatePresenter()
    {
        var presenter = base.CreatePresenter();
        if (!DesignTokens.ShaderDegrade)
            presenter.Classes.Add("popup-enter");
        return presenter;
    }
}
