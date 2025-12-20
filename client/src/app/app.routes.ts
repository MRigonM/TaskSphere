import { Routes } from '@angular/router';
import { HomeComponent } from './home/home.component';
import { LoginComponent } from './account/login/login.component';
import { RegisterComponent } from './account/register/register.component';
import { guestGuard } from './core/guards/guest.guard';

export const routes: Routes = [
  { path: '', component: HomeComponent },
  { path: 'account/login', component: LoginComponent, canActivate: [guestGuard] },
  { path: 'account/register', component: RegisterComponent, canActivate: [guestGuard] },
  { path: '**', redirectTo: '' },
];
