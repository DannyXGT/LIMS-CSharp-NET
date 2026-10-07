using System.ComponentModel;
using Lims.DesignSystem.Controls;
using Lims.Desktop.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;

namespace Lims.Desktop.Views;

public sealed partial class LoginPage : Page
{
    private bool _isPasswordVisible;
    private bool _identifierIsFocused;
    private bool _identifierIsHovered;
    private bool _passwordIsFocused;
    private bool _passwordIsHovered;

    public LoginPage(LoginViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        var enterAccelerator = new KeyboardAccelerator { Key = VirtualKey.Enter };
        enterAccelerator.Invoked += OnEnterInvoked;
        KeyboardAccelerators.Add(enterAccelerator);
    }

    public LoginViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        UpdateSubmitVisualState();
        UpdateInputVisualStates();
        UpdateMessageVisualState();
        HeroBrandTagline.Text = "Total Quality. Assured.";
        HeroHeadline.Text = "Gestión de\nlaboratorio,\npara un mundo\nmás seguro.";
        HeroSupport.Text = "Datos confiables.\nProcesos eficientes.\nResultados con impacto.";
        LoginEntrance.Begin();
        IdentifierBox.Focus(FocusState.Programmatic);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.CancelPendingSignIn();
    }

    private void OnEnterInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ViewModel.SignInCommand.CanExecute(null))
        {
            return;
        }

        ViewModel.SignInCommand.Execute(null);
        args.Handled = true;
    }

    private void OnTogglePasswordVisibility(object sender, RoutedEventArgs e)
    {
        _isPasswordVisible = !_isPasswordVisible;

        PasswordInput.PasswordRevealMode = _isPasswordVisible
            ? PasswordRevealMode.Visible
            : PasswordRevealMode.Hidden;

        var accessibleName = _isPasswordVisible
            ? "Ocultar contraseña"
            : "Mostrar contraseña";

        // Icon meaning follows the existing password visibility action.
        RevealPasswordGlyph.Icon = _isPasswordVisible ? LimsIconKind.HidePassword : LimsIconKind.ShowPassword;

        ToolTipService.SetToolTip(RevealPasswordButton, accessibleName);
        AutomationProperties.SetName(RevealPasswordButton, accessibleName);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoginViewModel.IsBusy))
        {
            DispatcherQueue.TryEnqueue(UpdateSubmitVisualState);
        }

        if (e.PropertyName == nameof(LoginViewModel.State))
        {
            DispatcherQueue.TryEnqueue(UpdateInputVisualStates);
        }

        if (e.PropertyName == nameof(LoginViewModel.HasMessage))
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                UpdateMessageVisualState();
                if (ViewModel.HasMessage)
                {
                    LoginError.Opacity = 0;
                    ErrorEntrance.Begin();
                }
            });
        }
    }

    private void UpdateSubmitVisualState() =>
        SubmitArrow.Visibility = ViewModel.IsBusy ? Visibility.Collapsed : Visibility.Visible;

    private void UpdateMessageVisualState()
    {
        LoginSubtitle.Visibility = ViewModel.HasMessage ? Visibility.Collapsed : Visibility.Visible;
        LoginForm.Margin = ViewModel.HasMessage
            ? new Thickness(0, 12, 0, 0)
            : new Thickness(0, 22, 0, 0);
    }

    private void OnIdentifierGotFocus(object sender, RoutedEventArgs e)
    {
        _identifierIsFocused = true;
        UpdateInputVisualStates();
    }

    private void OnIdentifierLostFocus(object sender, RoutedEventArgs e)
    {
        _identifierIsFocused = false;
        UpdateInputVisualStates();
    }

    private void OnIdentifierPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _identifierIsHovered = true;
        UpdateInputVisualStates();
    }

    private void OnIdentifierPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _identifierIsHovered = false;
        UpdateInputVisualStates();
    }

    private void OnPasswordGotFocus(object sender, RoutedEventArgs e)
    {
        _passwordIsFocused = true;
        UpdateInputVisualStates();
    }

    private void OnPasswordLostFocus(object sender, RoutedEventArgs e)
    {
        _passwordIsFocused = false;
        UpdateInputVisualStates();
    }

    private void OnPasswordPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _passwordIsHovered = true;
        UpdateInputVisualStates();
    }

    private void OnPasswordPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _passwordIsHovered = false;
        UpdateInputVisualStates();
    }

    private void UpdateInputVisualStates()
    {
        var hasCredentialError = ViewModel.State == LoginState.InvalidCredentials;

        ApplyInputState(
            IdentifierContainer,
            _identifierIsFocused,
            _identifierIsHovered,
            hasCredentialError);

        ApplyInputState(
            PasswordContainer,
            _passwordIsFocused,
            _passwordIsHovered,
            hasCredentialError);
    }

    private static void ApplyInputState(
        Border container,
        bool isFocused,
        bool isHovered,
        bool hasError)
    {
        var resourceKey = isFocused
            ? "LimsFocusBrush"
            : hasError
                ? "LimsErrorBrush"
                : isHovered
                    ? "LimsInputHoverBrush"
                    : "LimsInputBorderBrush";

        if (Application.Current.Resources.TryGetValue(resourceKey, out var resource) &&
            resource is Brush brush)
        {
            container.BorderBrush = brush;
        }
    }
}
