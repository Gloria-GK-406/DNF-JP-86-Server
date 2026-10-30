using Dfo.Server.Application.Commands;
using Dfo.Server.Application.HttpOperations;
using Microsoft.Extensions.DependencyInjection;

namespace Dfo.Server.Application.Administration;

public static class GmAdministrationCommandRegistration
{
    /// <summary>Each GM route's CMD; clean read cancellation and the failure audit are declared here, per command.</summary>
    public static IServiceCollection AddGmAdministrationCommands(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddInternalCommandHandler<LoginGmAdministratorCommand, HttpRouteReply, GmAccessHttpCommandHandler>();
        services.AddInternalCommandHandler<LogoutGmAdministratorCommand, HttpRouteReply, GmAccessHttpCommandHandler>();
        services.AddInternalCommandHandler<QueryGmAuditCommand, HttpRouteReply, GmAccessHttpCommandHandler>();

        services.AddInternalCommandHandler<QueryGmOverviewCommand, HttpRouteReply, GmPlayerDirectoryCommandHandler>();
        services.AddInternalCommandHandler<QueryGmAccountsCommand, HttpRouteReply, GmPlayerDirectoryCommandHandler>();
        services.AddInternalCommandHandler<QueryGmCharactersCommand, HttpRouteReply, GmPlayerDirectoryCommandHandler>();
        services.AddInternalCommandHandler<FindGmAccountCommand, HttpRouteReply, GmPlayerDirectoryCommandHandler>();
        services.AddInternalCommandHandler<FindGmCharacterCommand, HttpRouteReply, GmPlayerDirectoryCommandHandler>();

        services.AddInternalCommandHandler<QueryGmCeraCommand, HttpRouteReply, GmCeraCommandHandler>();
        services.AddInternalCommandHandler<GrantGmCeraCommand, HttpRouteReply, GmCeraCommandHandler>(failureAudit: CommandFailureAuditCapability.AuditedFailure);

        services.AddInternalCommandHandler<QueryGmMailCommand, HttpRouteReply, GmOfficialMailCommandHandler>();
        services.AddInternalCommandHandler<EditGmMailCommand, HttpRouteReply, GmOfficialMailCommandHandler>();
        services.AddInternalCommandHandler<SendGmMailCommand, HttpRouteReply, GmOfficialMailCommandHandler>(failureAudit: CommandFailureAuditCapability.AuditedFailure);

        services.AddInternalCommandHandler<QueryGmInitializationCommand, HttpRouteReply, GmInitializationCommandHandler>();
        services.AddInternalCommandHandler<SaveGmAccountInitializationCommand, HttpRouteReply, GmInitializationCommandHandler>();
        services.AddInternalCommandHandler<SaveGmCharacterInitializationCommand, HttpRouteReply, GmInitializationCommandHandler>();
        services.AddInternalCommandHandler<SearchGmInitializationItemsCommand, HttpRouteReply, GmInitializationCommandHandler>();
        services.AddInternalCommandHandler<ReadGmInitializationItemCommand, HttpRouteReply, GmInitializationCommandHandler>();

        services.AddInternalCommandHandler<QueryGmDailyRefillCommand, HttpRouteReply, GmDailyRefillCommandHandler>();
        services.AddInternalCommandHandler<ChangeGmDailyRefillCommand, HttpRouteReply, GmDailyRefillCommandHandler>();
        services.AddInternalCommandHandler<SearchGmDailyRefillItemsCommand, HttpRouteReply, GmDailyRefillCommandHandler>();
        services.AddInternalCommandHandler<ReadGmDailyRefillItemCommand, HttpRouteReply, GmDailyRefillCommandHandler>();

        services.AddInternalCommandHandler<QueryGmEventsCommand, HttpRouteReply, GmEventsCommandHandler>(ReadOnlyActionCancellationCapability.ConfirmedRollbackRead);
        services.AddInternalCommandHandler<QueryGmWeeklyAttendanceSettingsCommand, HttpRouteReply, GmEventsCommandHandler>(ReadOnlyActionCancellationCapability.ConfirmedRollbackRead);
        services.AddInternalCommandHandler<ChangeGmEventsCommand, HttpRouteReply, GmEventsCommandHandler>();
        services.AddInternalCommandHandler<QueryGmJoustSettingsCommand, HttpRouteReply, GmEventsCommandHandler>(ReadOnlyActionCancellationCapability.ConfirmedRollbackRead);
        services.AddInternalCommandHandler<QueryGmJoustCurrentRoundCommand, HttpRouteReply, GmEventsCommandHandler>(ReadOnlyActionCancellationCapability.ConfirmedRollbackRead);
        services.AddInternalCommandHandler<ChangeGmJoustSettingsCommand, HttpRouteReply, GmEventsCommandHandler>();
        services.AddInternalCommandHandler<ChangeGmJoustChampionCommand, HttpRouteReply, GmEventsCommandHandler>();
        return services;
    }
}
