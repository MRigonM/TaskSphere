import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import {AuthStoreService} from '../services/auth-store.service';

export const companyGuard: CanActivateFn = () => {
  const auth = inject(AuthStoreService);
  const router = inject(Router);

  if (!auth.isLoggedIn()) {
    router.navigateByUrl('/account/login');
    return false;
  }

  if (!auth.isCompany()) {
    router.navigateByUrl('/');
    return false;
  }

  return true;
};
