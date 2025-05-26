using AssettoServer.Server;
using AssettoServer.Server.Plugin;
using Autofac;
using TougePlugin.Models;
using TougePlugin.TougeRulesets;

namespace TougePlugin;

public class TougeModule : AssettoServerModule<TougeConfiguration>
{
    protected override void Load(ContainerBuilder builder)
    {
        builder.RegisterType<Touge>().AsSelf().As<IAssettoServerAutostart>().SingleInstance();
        builder.RegisterType<EntryCarTougeSession>().AsSelf();
        builder.RegisterType<TougeSession>().AsSelf();
        builder.RegisterType<Race>().AsSelf();

        // Rulesets
        builder.RegisterType<BattleStageRuleset>().As<ITougeRuleset>().Keyed<ITougeRuleset>(RulesetType.BattleStage);
        builder.RegisterType<CatAndMouseRuleset>().As<ITougeRuleset>().Keyed<ITougeRuleset>(RulesetType.CatAndMouse);

        builder.Register<Func<RulesetType, ITougeRuleset>>(c =>
        {
            var ctx = c.Resolve<IComponentContext>();
            return (rulesetType) => ctx.ResolveKeyed<ITougeRuleset>(rulesetType);
        });


        builder.Register<Func<EntryCar, EntryCar, ITougeRuleset, TougeSession>>(c =>
        {
            var ctx = c.Resolve<IComponentContext>();
            return (challenger, challenged, ruleset) =>
                ctx.Resolve<TougeSession>(
                    new NamedParameter("challenger", challenger),
                    new NamedParameter("challenged", challenged),
                    new NamedParameter("ruleset", ruleset));
        });
    }
}
