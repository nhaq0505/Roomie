// Native SVG Visualizations
// Zero external chart dependencies. AntD components and native SVG only.

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
          {/* Subtle grid lines */}
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

        {/* Labels underneath */}
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

export function NativeLatencyHistogram({
  buckets,
  height = 140,
}: {
  buckets: { range: string; count: number; isViolating?: boolean }[];
  height?: number;
}) {
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
