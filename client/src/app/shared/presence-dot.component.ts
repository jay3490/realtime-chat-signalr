import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'app-presence-dot',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<span
    class="presence-dot"
    [class.online]="online()"
    [attr.title]="online() ? 'Online' : 'Offline'"
    [attr.aria-label]="online() ? 'Online' : 'Offline'"
    role="img"
  ></span>`,
})
export class PresenceDotComponent {
  readonly online = input.required<boolean>();
}
