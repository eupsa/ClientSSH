namespace ClientSSH.ViewModels;

public sealed class WelcomeViewModel(TourViewModel tour)
{
    public TourViewModel Tour { get; } = tour;
}
