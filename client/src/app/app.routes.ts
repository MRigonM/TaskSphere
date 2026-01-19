import { Routes } from '@angular/router';
import { HomeComponent } from './home/home.component';
import { LoginComponent } from './account/login/login.component';
import { RegisterComponent } from './account/register/register.component';
import { guestGuard } from './core/guards/guest.guard';
import {UsersDashboardComponent} from './company-dashboard/users/users-dashboard.component';
import {companyGuard} from './core/guards/company.guard';
import {ProjectComponent} from './company-dashboard/projects/projects.component';
import {SprintsPageComponent} from './sprints/sprints-page.component';
import {TasksPageComponent} from './tasks/tasks-page.component';
import {companyMemberGuard} from './core/guards/company-member.guard';
import {ProjectPageComponent} from './company-dashboard/projects/project-page.component';
import {ProjectSprintsComponent} from './company-dashboard/projects/project-sprints.component';

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
    canActivate: [companyMemberGuard],
  },
  {
    path: 'dashboard/projects/:projectId',
    component: ProjectPageComponent,
    canActivate: [companyGuard]
  },
  {
    path: 'dashboard/projects/:projectId/sprints',
    component: ProjectSprintsComponent,
    canActivate: [companyGuard],
  },
  {
    path: 'sprints/:projectId',
    component: SprintsPageComponent,
    canActivate: [companyMemberGuard]
  },
  {
    path: 'tasks/:projectId',
    component: TasksPageComponent,
    canActivate: [companyMemberGuard],
  },
  { path: '**', redirectTo: '' },
];
