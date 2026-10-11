// Shapes returned by the AssetPro API. Dates arrive as ISO strings (UTC).

export type AssetStatus = 'InStock' | 'Assigned' | 'UnderRepair' | 'Damaged' | 'Scrapped' | 'Retired';
export type AssetCondition = 'New' | 'Good' | 'Fair' | 'Damaged' | 'Critical';
export type RequestType = 'Repair' | 'Replacement' | 'NewAsset' | 'Return' | 'Other';
export type RequestStatus = 'Submitted' | 'Approved' | 'Rejected' | 'InProgress' | 'Delivered' | 'Checking' | 'Received' | 'Done';
export type AssetAction =
  | 'Created' | 'StockIn' | 'Assigned' | 'Returned' | 'RepairRequested' | 'RepairStarted' | 'RepairCompleted'
  | 'ComponentReplaced' | 'Damaged' | 'Scrapped' | 'Retired' | 'StatusChanged' | 'Updated';
export type InventoryTransactionType = 'StockIn' | 'StockOut' | 'Return' | 'Scrap' | 'Adjustment';
export type UserRole = 'Admin' | 'User';
export type EmployeeStatus = 'Active' | 'Inactive';

export interface PagedResult<T> {
  items: T[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface ApiError {
  status: number;
  title: string;
  message: string;
  traceId?: string;
  errors?: Record<string, string[]> | null;
}

export interface SessionUser {
  id: number;
  email: string;
  fullName: string;
  role: UserRole;
  employeeId: number | null;
  permissions: string[];
}

export interface LoginResponse {
  token: string;
  expiresAt: string;
  user: SessionUser;
}

export interface Lookup {
  id: number;
  name: string;
  isActive: boolean;
  tagPrefix: string | null;
  usageCount: number;
}

export interface AssetListItem {
  id: number;
  assetTag: string;
  name: string;
  assetTypeId: number;
  assetType: string;
  brandId: number | null;
  brand: string | null;
  model: string | null;
  serialNumber: string | null;
  purchaseDate: string | null;
  purchaseCost: number | null;
  warrantyExpiry: string | null;
  condition: AssetCondition;
  status: AssetStatus;
  locationId: number | null;
  location: string | null;
  assignedToEmployeeId: number | null;
  assignedTo: string | null;
  assignedAt: string | null;
  createdAt: string;
}

export interface CurrentAssignment {
  id: number;
  employeeId: number;
  employeeName: string;
  employeeCode: string;
  assignedAt: string;
  assignedBy: string;
  conditionAtAssignment: AssetCondition;
  notes: string | null;
}

export interface AssignmentRecord {
  id: number;
  employeeId: number;
  employeeName: string;
  assignedAt: string;
  returnedAt: string | null;
  conditionAtAssignment: AssetCondition;
  returnCondition: AssetCondition | null;
  damageDetails: string | null;
}

export interface ComponentReplacement {
  id: number;
  component: string;
  oldComponent: string | null;
  newComponent: string;
  reason: string;
  replacedOn: string;
  performedBy: string;
  notes: string | null;
}

export interface ScrapRecord {
  id: number;
  scrapDate: string;
  reason: string;
  quantity: number;
  notes: string | null;
  performedBy: string;
}

export interface AssetDetail {
  asset: AssetListItem;
  notes: string | null;
  inventoryItemId: number;
  currentAssignment: CurrentAssignment | null;
  assignments: AssignmentRecord[];
  replacements: ComponentReplacement[];
  scrap: ScrapRecord | null;
}

export interface AssetHistoryItem {
  id: number;
  occurredAt: string;
  assetId: number;
  assetTag: string;
  assetName: string;
  assetType: string;
  employeeId: number | null;
  employeeName: string | null;
  action: AssetAction;
  previousStatus: AssetStatus | null;
  newStatus: AssetStatus | null;
  performedBy: string | null;
  notes: string | null;
}

export interface InventoryItem {
  id: number;
  name: string;
  assetTypeId: number;
  assetType: string;
  brandId: number | null;
  brand: string | null;
  model: string | null;
  reorderLevel: number;
  total: number;
  inStock: number;
  assigned: number;
  underRepair: number;
  damaged: number;
  scrapped: number;
  retired: number;
  isLowStock: boolean;
}

export interface InventoryTransaction {
  id: number;
  occurredAt: string;
  inventoryItemId: number;
  itemName: string;
  assetType: string;
  type: InventoryTransactionType;
  quantity: number;
  balanceAfter: number;
  assetId: number | null;
  assetTag: string | null;
  employeeId: number | null;
  employeeName: string | null;
  performedBy: string | null;
  notes: string | null;
}

export interface StockMovementResult {
  inventoryItemId: number;
  quantity: number;
  assetTags: string[];
  inStockAfter: number;
}

export interface Employee {
  id: number;
  employeeCode: string;
  fullName: string;
  email: string;
  contactNumber: string | null;
  companyName: string | null;
  departmentId: number | null;
  department: string | null;
  designationId: number | null;
  designation: string | null;
  role: UserRole | null;
  userId: number | null;
  status: EmployeeStatus;
  assignedAssets: number;
  createdAt: string;
}

export interface EmployeeOption {
  id: number;
  fullName: string;
  employeeCode: string;
  department: string | null;
}

export interface EmployeeProfile {
  employee: Employee;
  assets: AssetListItem[];
  history: AssetHistoryItem[];
  requests: RequestListItem[];
}

export interface RequestListItem {
  id: number;
  requestNumber: string;
  type: RequestType;
  status: RequestStatus;
  assetId: number | null;
  assetTag: string | null;
  assetName: string | null;
  requesterEmployeeId: number;
  requesterName: string;
  department: string | null;
  comments: string;
  createdAt: string;
  updatedAt: string;
}

export interface RequestStatusHistory {
  id: number;
  fromStatus: RequestStatus | null;
  toStatus: RequestStatus;
  changedBy: string;
  note: string | null;
  changedAt: string;
}

export interface RequestDetail {
  request: RequestListItem;
  history: RequestStatusHistory[];
  allowedActions: string[];
}

export interface NamedCount { name: string; count: number; }
export interface MonthlyPoint { month: string; primary: number; secondary: number; }
export interface LowStockItem { id: number; name: string; assetType: string; inStock: number; reorderLevel: number; }

export interface Dashboard {
  totalAssets: number;
  assignedAssets: number;
  availableAssets: number;
  underRepair: number;
  damaged: number;
  scrapped: number;
  totalEmployees: number;
  pendingRequests: number;
  openRequests: number;
  warrantyExpiringSoon: number;
  portfolioValue: number;
  statusBreakdown: NamedCount[];
  typeBreakdown: NamedCount[];
  requestStatusBreakdown: NamedCount[];
  requestTrend: MonthlyPoint[];
  assignmentTrend: MonthlyPoint[];
  recentActivity: AssetHistoryItem[];
  recentRequests: RequestListItem[];
  lowStock: LowStockItem[];
}

export interface ActivityLog {
  id: number;
  occurredAt: string;
  userId: number | null;
  userEmail: string | null;
  userName: string | null;
  action: string;
  entity: string | null;
  entityId: string | null;
  description: string;
  ipAddress: string | null;
  result: 'Success' | 'Failure';
}

export interface ApplicationLog {
  id: number;
  timestamp: string;
  level: string;
  category: string;
  message: string;
  exception: string | null;
  traceId: string | null;
  requestPath: string | null;
}

export interface UserAccount {
  id: number;
  email: string;
  fullName: string | null;
  employeeId: number | null;
  employeeCode: string | null;
  role: UserRole;
  isActive: boolean;
  lastLoginAt: string | null;
  createdAt: string;
  grantedPermissions: string[];
}

export interface AppSettings {
  companyName: string;
  currency: string;
  defaultPageSize: number;
  warrantyAlertDays: number;
}

export interface AboutInfo {
  product: string;
  version: string;
  environment: string;
  database: string;
  assets: number;
  employees: number;
  users: number;
  serverTimeUtc: string;
}

export interface EmployeeAssetReportRow {
  id: number;
  employeeId: number;
  employeeCode: string;
  employeeName: string;
  department: string | null;
  assetId: number;
  assetTag: string;
  assetName: string;
  assetType: string;
  assignedAt: string;
  returnedAt: string | null;
  isReturned: boolean;
  conditionAtAssignment: AssetCondition;
  returnCondition: AssetCondition | null;
  assignedBy: string;
}

export interface ScrapReportRow {
  id: number;
  scrapDate: string;
  assetId: number;
  assetTag: string;
  assetName: string;
  assetType: string;
  reason: string;
  quantity: number;
  purchaseCost: number | null;
  performedBy: string;
  notes: string | null;
}
