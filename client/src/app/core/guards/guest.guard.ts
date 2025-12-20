import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthStoreService } from '../services/auth-store.service';

export const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthStoreService);
  const router = inject(Router);

  if (auth.isLoggedIn()) {
    return router.parseUrl('/');
  }

  return true;
};
