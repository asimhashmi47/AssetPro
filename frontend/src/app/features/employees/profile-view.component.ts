import { ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../../core/auth.service';
import { avatarClass, initials } from '../../core/labels';
import { EmployeeProfile } from '../../core/models';
import { IconComponent } from '../../shared/icon.component';
import { HistoryTimelineComponent } from '../../shared/timeline.component';
import { BadgeComponent, EmptyStateComponent, LocalDatePipe } from '../../shared/ui';

/** Profile summary + assigned assets + timeline + requests. Used for employee profiles and "My Workspace". */
@Component({
  selector: 'app-profile-view',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouterLink, IconComponent, HistoryTimelineComponent, BadgeComponent, EmptyStateComponent, LocalDatePipe],
  templateUrl: './profile-view.component.html',
  styleUrl: './profile-view.component.css'
})
export class ProfileViewComponent {
  protected readonly auth = inject(AuthService);

  readonly profile = input.required<EmployeeProfile>();
  readonly self = input(false);
  readonly edit = output<void>();
  readonly remove = output<void>();
  readonly newRequest = output<number | null>();

  protected readonly tab = signal<'timeline' | 'requests'>('timeline');

  protected readonly e = computed(() => this.profile().employee);
  protected readonly initials = computed(() => initials(this.e().fullName));
  protected readonly avatar = computed(() => avatarClass(this.e().fullName));
  protected readonly openRequests = computed(() => this.profile().requests.filter(r => r.status !== 'Done' && r.status !== 'Rejected').length);
  protected readonly typeIcon = (type: string) =>
    type === 'Laptop' || type === 'Desktop Computer' ? 'laptop' : type.startsWith('Monitor') ? 'monitor' : type === 'Mobile Phone' || type === 'Tablet' ? 'tag' : 'box';

  constructor() {
    // Opening another person's profile starts on their timeline, not the tab left over from the last one.
    const employeeId = computed(() => this.e().id);
    effect(() => {
      employeeId();
      this.tab.set('timeline');
    });
  }
}
