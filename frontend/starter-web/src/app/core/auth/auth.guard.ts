import { inject } from '@angular/core';
import { Router, type CanActivateFn } from '@angular/router';
import { loginTree } from '../routing/route-trees';
import { AuthService } from './auth.service';

/** Lets a signed-in user through; sends anyone else to the login page, remembering the target URL. */
export const authGuard: CanActivateFn = (_route, state) => {
  const router = inject(Router);
  return inject(AuthService).isAuthenticated() ? true : loginTree(router, state.url);
};
