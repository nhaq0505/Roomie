// Native SVG Visualizations
// Zero external chart dependencies. AntD components and native SVG only.
import { t } from '../../../shared/i18n/runtime';

export interface DataPoint {
  label: string;
  value: number;
  secondaryValue?: number;
}

export function NativeSparkline({
  data,
  color = '#17a486',
  height = 42,
  width = 120,
}: {
  data: number[];
  color?: string;
  height?: number;
  width?: number;
}) {
  if (!data || data.length < 2) {
    return <span style={{ color: '#8c9ea2', fontSize: 12 }}>—</span>;
  }

  const min = Math.min(...data);
  const max = Math.max(...data);
  const range = max - min || 1;
  const padding = 4;
  const usableHeight = height - padding * 2;
  const stepX = (width - padding * 2) / (data.length - 1);

  const points = data.map((val, idx) => {
    const x = padding + idx * stepX;
    const y = height - padding - ((val - min) / range) * usableHeight;
    return `${x},${y}`;
  });

  const pathD = `M ${points.join(' L ')}`;
  const areaD = `${pathD} L ${width - padding},${height} L ${padding},${height} Z`;

  return (
    <svg width={width} height={height} style={{ overflow: 'visible' }}>
      <defs>
        <linearGradient id={`sparkGrad-${color.replace('#', '')}`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor={color} stopOpacity="0.25" />
          <stop offset="100%" stopColor={color} stopOpacity="0.0" />
        </linearGradient>
      </defs>
      <path d={areaD} fill={`url(#sparkGrad-${color.replace('#', '')})`} />
      <path d={pathD} fill="none" stroke={color} strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

export function NativeBarChart({
  items,
  height = 180,
  barColor = '#17a486',
  valueFormatter = (val) => String(val),
}: {
  items: DataPoint[];
  height?: number;
  barColor?: string;
  valueFormatter?: (val: number) => string;
}) {
  if (!items || items.length === 0) {
    return (
      <div style={{ height, display: 'grid', placeItems: 'center', color: '#8c9ea2' }}>
        <span>—</span>
      </div>
    );
  }

  const maxValue = Math.max(...items.map((i) => i.value), 1);
  const chartHeight = height - 36;

  return (
    <div style={{ width: '100%', overflowX: 'auto' }}>
      <div style={{ minWidth: items.length * 48, height }}>
        <svg width="100%" height={chartHeight} style={{ overflow: 'visible' }}>
          <line x1="0" y1="0" x2="100%" y2="0" stroke="#f0f3f2" strokeDasharray="3 3" />
          <line x1="0" y1={chartHeight / 2} x2="100%" y2={chartHeight / 2} stroke="#f0f3f2" strokeDasharray="3 3" />
          <line x1="0" y1={chartHeight} x2="100%" y2={chartHeight} stroke="#e5eeeb" strokeWidth="1" />

          {items.map((item, idx) => {
            const barWidth = 24;
            const xPercent = (idx + 0.5) * (100 / items.length);
            const barH = (item.value / maxValue) * (chartHeight - 16);
            const barY = chartHeight - barH;

            return (
              <g key={item.label}>
                <rect
                  x={`calc(${xPercent}% - ${barWidth / 2}px)`}
                  y={barY}
                  width={barWidth}
                  height={Math.max(barH, 3)}
                  rx="4"
                  fill={barColor}
                  className="native-bar-element"
                >
                  <title>{`${item.label}: ${valueFormatter(item.value)}`}</title>
                </rect>
                <text
                  x={`${xPercent}%`}
                  y={barY - 5}
                  textAnchor="middle"
                  fill="#50676d"
                  fontSize="11"
                  fontWeight="600"
                >
                  {valueFormatter(item.value)}
                </text>
              </g>
            );
          })}
        </svg>

        <div style={{ display: 'flex', justifyContent: 'space-around', paddingTop: 8 }}>
          {items.map((item) => (
            <div
              key={item.label}
              style={{
                flex: 1,
                textAlign: 'center',
                fontSize: 11,
                color: '#7f9095',
                overflow: 'hidden',
                textOverflow: 'ellipsis',
                whiteSpace: 'nowrap',
                padding: '0 4px',
              }}
              title={item.label}
            >
              {item.label}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

export interface FunnelStage {
  label: string;
  count: number | null;
  color?: string;
}

export function NativeConversionFunnel({ stages }: { stages: FunnelStage[] }) {
  if (!stages || stages.length === 0) return null;

  const topStageCount = stages[0]?.count ?? null;
  const maxKnownCount = stages.reduce<number>(
    (max, s) => (s.count !== null && s.count !== undefined && s.count > max ? s.count : max),
    0
  ) || 1;

  return (
    <div className="native-funnel" style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
      {stages.map((stage, idx) => {
        const isCountKnown = stage.count !== null && stage.count !== undefined;
        const prevStage = idx > 0 ? stages[idx - 1] : null;
        const prevCountKnown = prevStage !== null && prevStage.count !== null && prevStage.count !== undefined;

        let stepRate: number | null = null;
        if (isCountKnown && prevCountKnown && prevStage.count! > 0) {
          stepRate = Math.min(Math.round((stage.count! / prevStage.count!) * 100), 100);
        }

        let percentOfTop: number | null = null;
        if (isCountKnown && topStageCount !== null && topStageCount > 0) {
          percentOfTop = Math.min(Math.round((stage.count! / topStageCount) * 100), 100);
        }

        const barBasis = topStageCount !== null && topStageCount > 0 ? topStageCount : maxKnownCount;
        const visualBarWidth = isCountKnown ? Math.min(Math.round((stage.count! / barBasis) * 100), 100) : 0;
        const color = stage.color || (idx === 0 ? '#17a486' : idx === 1 ? '#34757c' : idx === 2 ? '#bb8133' : '#457b9d');

        return (
          <div key={stage.label} style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
            <div style={{ display: 'flex', justifyContent: 'space-between', fontSize: 13 }}>
              <span>
                <strong>{stage.label}</strong>
                {stepRate !== null && (
                  <span style={{ marginLeft: 8, color: '#7a8c91', fontSize: 12 }}>
                    ({stepRate}% {t("từ bước trước")})
                  </span>
                )}
              </span>
              <span>
                <strong>{isCountKnown ? stage.count!.toLocaleString() : '—'}</strong>
                {percentOfTop !== null ? (
                  <span style={{ marginLeft: 6, color: '#7a8c91', fontSize: 12 }}>
                    ({percentOfTop}%)
                  </span>
                ) : (
                  <span style={{ marginLeft: 6, color: '#7a8c91', fontSize: 12 }}>
                    (—)
                  </span>
                )}
              </span>
            </div>
            <div style={{ width: '100%', height: 18, background: '#f0f4f3', borderRadius: 9, overflow: 'hidden' }}>
              <div
                style={{
                  width: isCountKnown ? `${Math.max(visualBarWidth, 2)}%` : '0%',
                  height: '100%',
                  background: isCountKnown ? color : 'transparent',
                  borderRadius: 9,
                  transition: 'width 0.3s ease',
                }}
              />
            </div>
          </div>
        );
      })}
    </div>
  );
}

export interface TrendSeries {
  name: string;
  color: string;
  data: (number | null)[];
}

export function NativeMultiLineChart({
  dates,
  series,
  height = 200,
}: {
  dates: string[];
  series: TrendSeries[];
  height?: number;
}) {
  if (!dates || dates.length === 0 || !series || series.length === 0) {
    return (
      <div style={{ height, display: 'grid', placeItems: 'center', color: '#8c9ea2' }}>
        <span>—</span>
      </div>
    );
  }

  const allValues = series.flatMap((s) => s.data.filter((v): v is number => v !== null && v !== undefined));
  const maxVal = allValues.length > 0 ? Math.max(...allValues, 1) : 1;
  const minVal = 0;
  const range = maxVal - minVal || 1;

  const chartHeight = height - 40;
  const paddingLeft = 40;
  const paddingRight = 16;
  const paddingTop = 12;
  const paddingBottom = 20;

  const innerWidth = 1000;
  const innerHeight = chartHeight - paddingTop - paddingBottom;
  const stepX = (innerWidth - paddingLeft - paddingRight) / Math.max(dates.length - 1, 1);

  return (
    <div style={{ width: '100%', overflowX: 'auto' }}>
      <div style={{ minWidth: 480 }}>
        {/* Legend */}
        <div style={{ display: 'flex', gap: 16, marginBottom: 8, flexWrap: 'wrap', fontSize: 12 }}>
          {series.map((s) => (
            <div key={s.name} style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
              <span style={{ width: 12, height: 3, background: s.color, borderRadius: 2 }} />
              <span style={{ color: '#55696e' }}>{s.name}</span>
            </div>
          ))}
        </div>

        <svg width="100%" height={chartHeight} viewBox={`0 0 ${innerWidth} ${chartHeight}`} preserveAspectRatio="none" style={{ overflow: 'visible' }}>
          {/* Grid lines */}
          <line x1={paddingLeft} y1={paddingTop} x2={innerWidth - paddingRight} y2={paddingTop} stroke="#f0f4f3" strokeDasharray="3 3" />
          <line x1={paddingLeft} y1={paddingTop + innerHeight / 2} x2={innerWidth - paddingRight} y2={paddingTop + innerHeight / 2} stroke="#f0f4f3" strokeDasharray="3 3" />
          <line x1={paddingLeft} y1={paddingTop + innerHeight} x2={innerWidth - paddingRight} y2={paddingTop + innerHeight} stroke="#e5eeeb" strokeWidth="1" />

          {/* Y Axis ticks */}
          <text x={paddingLeft - 8} y={paddingTop + 4} textAnchor="end" fontSize="11" fill="#8c9ea2">
            {maxVal.toLocaleString()}
          </text>
          <text x={paddingLeft - 8} y={paddingTop + innerHeight + 4} textAnchor="end" fontSize="11" fill="#8c9ea2">
            0
          </text>

          {/* Lines */}
          {series.map((s) => {
            const segments: string[][] = [];
            let currentSegment: string[] = [];
            s.data.forEach((val, idx) => {
              if (val !== null && val !== undefined) {
                const x = paddingLeft + idx * stepX;
                const y = paddingTop + innerHeight - ((val - minVal) / range) * innerHeight;
                currentSegment.push(`${x},${y}`);
              } else {
                if (currentSegment.length > 0) {
                  segments.push(currentSegment);
                  currentSegment = [];
                }
              }
            });
            if (currentSegment.length > 0) {
              segments.push(currentSegment);
            }

            return (
              <g key={s.name}>
                {segments.map((seg, sIdx) => (
                  <path
                    key={sIdx}
                    d={`M ${seg.join(' L ')}`}
                    fill="none"
                    stroke={s.color}
                    strokeWidth="2.5"
                    strokeLinecap="round"
                    strokeLinejoin="round"
                  />
                ))}
                {s.data.map((val, idx) => {
                  if (val === null || val === undefined) return null;
                  const x = paddingLeft + idx * stepX;
                  const y = paddingTop + innerHeight - ((val - minVal) / range) * innerHeight;
                  return (
                    <circle key={idx} cx={x} cy={y} r="3" fill="#fff" stroke={s.color} strokeWidth="2">
                      <title>{`${dates[idx]}: ${val.toLocaleString()}`}</title>
                    </circle>
                  );
                })}
              </g>
            );
          })}
        </svg>

        {/* Date labels */}
        <div style={{ display: 'flex', justifyContent: 'space-between', paddingLeft, paddingRight, paddingTop: 4 }}>
          {dates.length > 0 && (
            <span style={{ fontSize: 11, color: '#8c9ea2' }}>{dates[0]}</span>
          )}
          {dates.length > 2 && (
            <span style={{ fontSize: 11, color: '#8c9ea2' }}>{dates[Math.floor(dates.length / 2)]}</span>
          )}
          {dates.length > 1 && (
            <span style={{ fontSize: 11, color: '#8c9ea2' }}>{dates[dates.length - 1]}</span>
          )}
        </div>
      </div>
    </div>
  );
}

export function NativeLatencyHistogram({
  buckets,
  height = 140,
}: {
  buckets?: { range: string; count: number; isViolating?: boolean }[];
  height?: number;
}) {
  if (!buckets || buckets.length === 0) {
    return (
      <div style={{ height, display: 'grid', placeItems: 'center', color: '#8c9ea2' }}>
        <span>{t("Chưa có dữ liệu phân phối độ trễ")}</span>
      </div>
    );
  }

  const maxCount = Math.max(...buckets.map((b) => b.count), 1);
  const chartHeight = height - 32;

  return (
    <div style={{ width: '100%', height }}>
      <svg width="100%" height={chartHeight} style={{ overflow: 'visible' }}>
        <line x1="0" y1={chartHeight} x2="100%" y2={chartHeight} stroke="#e5eeeb" strokeWidth="1" />
        {buckets.map((bucket, idx) => {
          const xPercent = (idx + 0.5) * (100 / buckets.length);
          const barWidth = 32;
          const barH = (bucket.count / maxCount) * (chartHeight - 14);
          const barY = chartHeight - barH;
          const fill = bucket.isViolating ? '#9f443b' : idx > 4 ? '#bb8133' : '#17a486';

          return (
            <g key={bucket.range}>
              <rect
                x={`calc(${xPercent}% - ${barWidth / 2}px)`}
                y={barY}
                width={barWidth}
                height={Math.max(barH, 3)}
                rx="4"
                fill={fill}
              >
                <title>{`${bucket.range}: ${bucket.count} requests`}</title>
              </rect>
              <text x={`${xPercent}%`} y={barY - 4} textAnchor="middle" fill="#50676d" fontSize="10" fontWeight="600">
                {bucket.count}
              </text>
            </g>
          );
        })}
      </svg>
      <div style={{ display: 'flex', justifyContent: 'space-around', paddingTop: 6 }}>
        {buckets.map((b) => (
          <div
            key={b.range}
            style={{
              flex: 1,
              textAlign: 'center',
              fontSize: 10,
              color: b.isViolating ? '#9f443b' : '#7f9095',
              fontWeight: b.isViolating ? 600 : 400,
            }}
          >
            {b.range}
          </div>
        ))}
      </div>
    </div>
  );
}
