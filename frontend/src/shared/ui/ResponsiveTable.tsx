import { useEffect, useRef, useState, type Key, type ReactNode } from 'react';
import { Pagination, Table, type TableProps } from 'antd';

/** Keep every field and action visible without horizontal scrolling on narrow screens. */
export default function ResponsiveTable<T extends object>(props: TableProps<T> & { renderCompact?: (record: T) => ReactNode; disableCompactPagination?: boolean }) {
  const container = useRef<HTMLDivElement>(null);
  const [compact, setCompact] = useState(true);
  const minimumWidth = typeof props.scroll?.x === 'number' ? props.scroll.x : 760;
  useEffect(() => {
    const element = container.current;
    if (!element) return;
    const observer = new ResizeObserver(([entry]) => setCompact(entry.contentRect.width < minimumWidth));
    observer.observe(element);
    return () => observer.disconnect();
  }, [minimumWidth]);
  const [page, setPage] = useState(1);
  const records = props.dataSource ?? [];
  const paging = typeof props.pagination === 'object' ? props.pagination : undefined;
  const controlled = paging?.current !== undefined;
  const pageSize = typeof props.pagination === 'object' ? props.pagination.pageSize ?? 10 : 10;
  useEffect(() => { setPage(1); }, [props.dataSource]);
  const { renderCompact, disableCompactPagination, ...tableProps } = props;
  if (!compact) return <div ref={container}><Table<T> {...tableProps} /></div>;
  const columns = (props.columns ?? []).filter((column) => !('children' in column));
  const cell = (column: typeof columns[number], record: T, index: number): ReactNode => {
    const field = 'dataIndex' in column ? column.dataIndex : undefined;
    const path = Array.isArray(field) ? field : field == null ? [] : [field];
    const value = path.reduce<unknown>((current, key) => current == null ? undefined : (current as Record<string, unknown>)[String(key)], record);
    return column.render ? column.render(value, record, index) as ReactNode : value as ReactNode;
  };
  const keyFor = (record: T, index: number) => typeof props.rowKey === 'function'
    ? props.rowKey(record, index)
    : (record as Record<string, Key>)[String(props.rowKey ?? 'key')] ?? index;
  return <div ref={container} className="mobile-records">
    {(disableCompactPagination || controlled ? records : records.slice((page - 1) * pageSize, page * pageSize)).map((record, index) => <article className="mobile-record" key={keyFor(record, index)}>
      {renderCompact ? renderCompact(record) : columns.map((column, colIndex) => <div key={String(column.key ?? colIndex)} className={colIndex === 0 ? 'mobile-record-heading' : column.title ? 'mobile-record-field' : 'mobile-record-actions'}>
        {colIndex > 0 && column.title && <span className="mobile-field-label">{column.title as ReactNode}</span>}
        <div className="mobile-field-value">{cell(column, record, index)}</div>
      </div>)}
    </article>)}
    {!disableCompactPagination && (controlled ? (paging?.total ?? 0) : records.length) > pageSize && <Pagination {...paging} current={controlled ? paging?.current : page} pageSize={pageSize} total={controlled ? paging?.total : records.length} onChange={controlled ? paging?.onChange : setPage} simple />}
  </div>;
}
