import { Routes } from '@angular/router';
import { HomeComponent } from './home/home.component';
import { LoginComponent } from './account/login/login.component';
import { RegisterComponent } from './account/register/register.component';
import { guestGuard } from './core/guards/guest.guard';
import {UsersDashboardComponent} from './company-dashboard/users/users-dashboard.component';
import {companyGuard} from './core/guards/company.guard';
import {ProjectComponent} from './company-dashboard/projects/projects.component';

export const routes: Routes = [
  { path: '', component: HomeComponent },
  { path: 'account/login', component: LoginComponent, canActivate: [guestGuard] },
  { path: 'account/register', component: RegisterComponent, canActivate: [guestGuard] },
  {
    path: 'dashboard/users',
    component: UsersDashboardComponent,
    canActivate: [companyGuard],
  },
  {
    path: 'dashboard/projects',
    component: ProjectComponent,
    canActivate: [companyGuard],
  },
  { path: '**', redirectTo: '' },
];
