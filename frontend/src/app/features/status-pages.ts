import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { EmptyStateComponent } from '../shared/ui';

@Component({
  selector: 'app-forbidden',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EmptyStateComponent, RouterLink],
  template: `
    <div class="card page">
      <app-empty-state title="You don’t have access to this page" icon="lock"
        message="Ask an administrator to grant you the required permission if you need it.">
        <a class="btn btn-primary" [routerLink]="auth.homeUrl()">Go to my home</a>
      </app-empty-state>
    </div>`
})
export class ForbiddenComponent {
  protected readonly auth = inject(AuthService);
}

@Component({
  selector: 'app-not-found',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [EmptyStateComponent, RouterLink],
  template: `
    <div class="card page">
      <app-empty-state title="Page not found" icon="search" message="The page you’re looking for doesn’t exist or has moved.">
        <a class="btn btn-primary" [routerLink]="auth.homeUrl()">Back to safety</a>
      </app-empty-state>
    </div>`
})
export class NotFoundComponent {
  protected readonly auth = inject(AuthService);
}
