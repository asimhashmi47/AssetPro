import { Routes } from '@angular/router';
import { authGuard, guestGuard, homeRedirect, permissionGuard } from './core/guards';
import { ShellComponent } from './layout/shell.component';

const needs = (...permissions: string[]) => ({ canActivate: [permissionGuard], data: { permissions } });

export const routes: Routes = [
  {
    path: 'login',
    canActivate: [guestGuard],
    title: 'Sign in · AssetPro',
    loadComponent: () => import('./features/auth/login.component').then(m => m.LoginComponent)
  },
  {
    path: '',
    component: ShellComponent,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', canActivate: [homeRedirect], children: [] },
      {
        path: 'dashboard', title: 'Dashboard · AssetPro', ...needs('Dashboard.View'),
        loadComponent: () => import('./features/dashboard/dashboard.component').then(m => m.DashboardComponent)
      },
      {
        path: 'my-workspace', title: 'My Workspace · AssetPro',
        loadComponent: () => import('./features/workspace/my-workspace.component').then(m => m.MyWorkspaceComponent)
      },
      {
        path: 'assets', title: 'Assets · AssetPro', ...needs('Assets.View'),
        loadComponent: () => import('./features/assets/assets-list.component').then(m => m.AssetsListComponent)
      },
      {
        // Holders can open their own assets; the API decides what they may see.
        path: 'assets/:id', title: 'Asset · AssetPro',
        loadComponent: () => import('./features/assets/asset-detail.component').then(m => m.AssetDetailComponent)
      },
      {
        path: 'inventory', title: 'Inventory · AssetPro', ...needs('Inventory.View'),
        loadComponent: () => import('./features/inventory/inventory.component').then(m => m.InventoryComponent)
      },
      {
        path: 'employees', title: 'Employees · AssetPro', ...needs('Employees.View'),
        loadComponent: () => import('./features/employees/employees-list.component').then(m => m.EmployeesListComponent)
      },
      {
        path: 'employees/:id', title: 'Employee · AssetPro', ...needs('Employees.View'),
        loadComponent: () => import('./features/employees/employee-profile.component').then(m => m.EmployeeProfileComponent)
      },
      {
        path: 'requests', title: 'Requests · AssetPro',
        loadComponent: () => import('./features/requests/requests-list.component').then(m => m.RequestsListComponent)
      },
      {
        path: 'requests/:id', title: 'Request · AssetPro',
        loadComponent: () => import('./features/requests/request-detail.component').then(m => m.RequestDetailComponent)
      },
      {
        path: 'history', title: 'Asset History · AssetPro', ...needs('Assets.View'),
        loadComponent: () => import('./features/history/history.component').then(m => m.HistoryComponent)
      },
      {
        path: 'reports', title: 'Reports · AssetPro', ...needs('Reports.View'),
        loadComponent: () => import('./features/reports/reports.component').then(m => m.ReportsComponent)
      },
      {
        path: 'activity-logs', title: 'Activity Logs · AssetPro', ...needs('ActivityLogs.View'),
        loadComponent: () => import('./features/activity-logs/activity-logs.component').then(m => m.ActivityLogsComponent)
      },
      {
        path: 'settings', title: 'Settings · AssetPro', ...needs('Settings.Manage', 'Users.Manage'),
        loadComponent: () => import('./features/settings/settings.component').then(m => m.SettingsComponent)
      },
      {
        path: 'account', title: 'Account · AssetPro',
        loadComponent: () => import('./features/auth/account.component').then(m => m.AccountComponent)
      },
      {
        path: 'forbidden', title: 'No access · AssetPro',
        loadComponent: () => import('./features/status-pages').then(m => m.ForbiddenComponent)
      },
      {
        path: '**', title: 'Not found · AssetPro',
        loadComponent: () => import('./features/status-pages').then(m => m.NotFoundComponent)
      }
    ]
  }
];
