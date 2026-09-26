namespace EcoFlow.EnergyManager;

public interface IEnergyPolicy
{
    EnergyDecision Evaluate(
        EcoFlowStatus status,
        SolarForecast forecast,
        DateTimeOffset nowUtc);
}
