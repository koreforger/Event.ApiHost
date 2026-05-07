using Event.FraudIntegrationControllers.CashoutOrchestrator.V1;
using Event.FraudIntegrationControllers.NedbankIdMFA.V1;
using Event.FraudIntegrationControllers.Party.V1;
using Event.FraudIntegrationControllers.Person.V1;
using Event.FraudIntegrationControllers.SASVIFinancialCrimeCaseManagement.V1;
using Event.FraudIntegrationControllers.SuspendAccount.V1;
using Event.FraudIntegrationControllers.SuspendAccount.V2;
using Event.FraudIntegrationControllers.UserDetails.V1;
using Event.FraudIntegrationControllers.UserFederationDetail.V1;
using Event.FraudIntegrationControllers.UserGroupFederationDetail.V1;
using Event.FraudIntegrationControllers.UserGroupFederationDetail.V2;
using Event.FraudIntegrationControllers.UserstateForNID.V1;
using Microsoft.Extensions.DependencyInjection;

namespace Event.ApiHost.Composition;

internal static class ControllerRegistration
{
    internal static IMvcBuilder AddFraudIntegrationControllers(this IMvcBuilder builder)
        => builder
            .AddCashoutOrchestratorV1Controllers()
            .AddNedbankIdMFAV1Controllers()
            .AddPartyV1Controllers()
            .AddPersonV1Controllers()
            .AddSASVIFinancialCrimeCaseManagementV1Controllers()
            .AddSuspendAccountV1Controllers()
            .AddSuspendAccountV2Controllers()
            .AddUserDetailsV1Controllers()
            .AddUserFederationDetailV1Controllers()
            .AddUserGroupFederationDetailV1Controllers()
            .AddUserGroupFederationDetailV2Controllers()
            .AddUserstateForNIDV1Controllers();
}
