import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { NavigationEnd, Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { filter } from 'rxjs';
import { AuthService } from '../core/auth.service';
import { ReferenceDataService } from '../core/reference-data.service';
import { NotifyService } from '../core/notify.service';
import { avatarClass, initials } from '../core/labels';
import { CreditComponent } from '../shared/credit.component';
import { IconComponent } from '../shared/icon.component';

interface NavItem {
  label: string;
  icon: string;
  link: string;
  /** Visible when the user holds any of these; empty = everyone. */
  permissions: string[];
  needsEmployee?: boolean;
}

interface NavGroup {
  title: string;
  items: NavItem[];
}

const NAV: NavGroup[] = [
  {
    title: 'Overview',
    items: [
      { label: 'Dashboard', icon: 'dashboard', link: '/dashboard', permissions: ['Dashboard.View'] },
      { label: 'My Workspace', icon: 'user', link: '/my-workspace', permissions: [], needsEmployee: true }
    ]
  },
  {
    title: 'Manage',
    items: [
      { label: 'Assets', icon: 'box', link: '/assets', permissions: ['Assets.View'] },
      { label: 'Inventory', icon: 'layers', link: '/inventory', permissions: ['Inventory.View'] },
      { label: 'Employees', icon: 'users', link: '/employees', permissions: ['Employees.View'] },
      { label: 'Requests', icon: 'inbox', link: '/requests', permissions: [] }
    ]
  },
  {
    title: 'Insights',
    items: [
      { label: 'Asset History', icon: 'history', link: '/history', permissions: ['Assets.View'] },
      { label: 'Reports', icon: 'chart', link: '/reports', permissions: ['Reports.View'] },
      { label: 'Activity Logs', icon: 'activity', link: '/activity-logs', permissions: ['ActivityLogs.View'] }
    ]
  },
  {
    title: 'System',
    items: [{ label: 'Settings', icon: 'settings', link: '/settings', permissions: ['Settings.Manage', 'Users.Manage'] }]
  }
];

@Component({
  selector: 'app-shell',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterOutlet, RouterLink, RouterLinkActive, IconComponent, CreditComponent],
  templateUrl: './shell.component.html',
  styleUrl: './shell.component.css',
  host: { '(document:click)': 'userMenu.set(false)' }
})
export class ShellComponent implements OnInit {
  protected readonly auth = inject(AuthService);
  protected readonly ref = inject(ReferenceDataService);
  private readonly notify = inject(NotifyService);
  private readonly router = inject(Router);

  protected readonly sidebarOpen = signal(false);
  protected readonly userMenu = signal(false);
  protected readonly year = new Date().getFullYear();

  protected readonly nav = computed(() => {
    const user = this.auth.user();
    return NAV.map(group => ({
      ...group,
      items: group.items.filter(item =>
        (item.permissions.length === 0 || this.auth.canAny(...item.permissions)) && (!item.needsEmployee || !!user?.employeeId))
    })).filter(group => group.items.length > 0);
  });

  protected readonly initials = computed(() => initials(this.auth.user()?.fullName));
  protected readonly avatarClass = computed(() => avatarClass(this.auth.user()?.fullName));

  constructor() {
    this.router.events.pipe(filter(e => e instanceof NavigationEnd), takeUntilDestroyed()).subscribe(() => this.sidebarOpen.set(false));
  }

  ngOnInit(): void {
    this.ref.loadSettings();
  }

  protected toggleUserMenu(event: Event): void {
    event.stopPropagation();
    this.userMenu.update(v => !v);
  }

  protected async logout(): Promise<void> {
    this.userMenu.set(false);
    if (await this.notify.confirm({ title: 'Sign out?', text: 'You will need to sign in again to continue.', confirmText: 'Sign out' })) {
      this.auth.logout();
    }
  }
}
