import type { HealthCheckStats } from "~/clients/backend-client.server";
import { Badge, Icon } from "~/components/ui";

export type HealthStatsProps = {
    stats: HealthCheckStats[];
}

enum HealthResult {
    Healthy = 0,
    Unhealthy = 1,
}

enum RepairAction {
    None = 0,
    Repaired = 1,
    Deleted = 2,
    ActionNeeded = 3,
}

export function HealthStats({ stats }: HealthStatsProps) {
    const totalChecked = stats
        .reduce((sum, stat) => sum + stat.count, 0);
    const healthy = stats
        .filter(stat => stat.result === HealthResult.Healthy)
        .reduce((sum, stat) => sum + stat.count, 0);
    const repaired = stats
        .filter(stat => stat.repairStatus === RepairAction.Repaired)
        .reduce((sum, stat) => sum + stat.count, 0);
    const deleted = stats
        .filter(stat => stat.repairStatus === RepairAction.Deleted)
        .reduce((sum, stat) => sum + stat.count, 0);

    const getPercentage = (count: number) => {
        return totalChecked > 0 ? Math.round((count / totalChecked) * 100) : 0;
    };

    return (
        <section className="card w-full border border-base-content/10 bg-base-100 shadow-sm">
            <div className="card-body gap-4 p-4 md:p-6">
                <div className="flex flex-wrap items-center justify-between gap-4">
                    <h2 className="card-title text-xl">Overview</h2>
                    <Badge className="badge-ghost">Last 30 Days</Badge>
                </div>

                <div className="stats stats-vertical w-full shadow sm:stats-horizontal">
                    <div className="stat place-items-center">
                        <div className="stat-figure text-base-content/50">
                            <Icon name="fact_check" className="!text-[22px]" />
                        </div>
                        <div className="stat-title">Total Checked</div>
                        <div className="stat-value font-mono text-3xl md:text-4xl">{totalChecked}</div>
                    </div>

                    <div className="stat place-items-center">
                        <div className="stat-figure text-success">
                            <Icon name="check_circle" filled className="!text-[22px]" />
                        </div>
                        <div className="stat-title">Healthy ({getPercentage(healthy)}%)</div>
                        <div className="stat-value font-mono text-3xl text-success md:text-4xl">{healthy}</div>
                    </div>

                    <div className="stat place-items-center">
                        <div className="stat-figure text-info">
                            <Icon name="build_circle" filled className="!text-[22px]" />
                        </div>
                        <div className="stat-title">Repaired ({getPercentage(repaired)}%)</div>
                        <div className="stat-value font-mono text-3xl text-info md:text-4xl">{repaired}</div>
                    </div>

                    <div className="stat place-items-center">
                        <div className="stat-figure text-error">
                            <Icon name="delete" filled className="!text-[22px]" />
                        </div>
                        <div className="stat-title">Deleted ({getPercentage(deleted)}%)</div>
                        <div className="stat-value font-mono text-3xl text-error md:text-4xl">{deleted}</div>
                    </div>
                </div>
            </div>
        </section>
    );
}
