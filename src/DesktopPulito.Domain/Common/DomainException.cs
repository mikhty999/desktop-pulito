namespace DesktopPulito.Domain;

/// <summary>Violazione di una regola del dominio.</summary>
public sealed class DomainException(string message) : Exception(message);

