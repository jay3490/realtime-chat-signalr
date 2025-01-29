import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const signedInGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.accessToken ? true : inject(Router).createUrlTree(['/login']);
};
