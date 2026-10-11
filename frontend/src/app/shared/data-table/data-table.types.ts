import { Option } from '../../core/labels';

export type ColumnFormat = 'text' | 'mono' | 'badge' | 'date' | 'datetime' | 'money' | 'number' | 'bool';

export interface TableColumn {
  /** DTO property name - also the sort/filter key sent to the API. */
  key: string;
  label: string;
  format?: ColumnFormat;
  /** Options for badge (enum) filters. */
  options?: Option[];
  sortable?: boolean;
  filterable?: boolean;
  /** Hidden by default (user can show it from the Columns menu). */
  hidden?: boolean;
  /** Cannot be hidden. */
  locked?: boolean;
  align?: 'start' | 'end' | 'center';
  width?: string;
  /** Custom CSV value; defaults to the raw/formatted cell value. */
  exportValue?: (row: any) => string | number | null | undefined;
}
