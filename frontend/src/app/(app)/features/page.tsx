"use client";

import * as React from "react";
import { PlusIcon, PencilIcon, Trash2Icon, XIcon } from "lucide-react";
import { api } from "@/lib/api/client";
import type { FeatureDto, FeatureStatus, ProjectDto } from "@/lib/api/types";
import { FEATURE_STATUSES } from "@/lib/api/types";
import {
  queryKeys,
  useFeatures,
  useInvalidatingMutation,
  useProjects,
  useWorkspace,
} from "@/lib/queries";
import { PageHeader, EmptyState } from "@/components/page-header";
import { ConfirmDialog } from "@/components/confirm-dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Badge } from "@/components/ui/badge";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from "@/components/ui/table";

const STATUS_BADGE: Record<FeatureStatus, "outline" | "secondary" | "default"> = {
  Hypothetical: "outline",
  Planned: "secondary",
  Shipped: "default",
};

export default function FeaturesPage() {
  const workspace = useWorkspace();
  const workspaceId = workspace.data?.id;
  const features = useFeatures(workspaceId);
  const projects = useProjects(workspaceId);

  const [editorOpen, setEditorOpen] = React.useState(false);
  const [editing, setEditing] = React.useState<FeatureDto | null>(null);
  const [deleting, setDeleting] = React.useState<FeatureDto | null>(null);

  const projectsByFeature = React.useMemo(() => {
    const map = new Map<string, ProjectDto[]>();
    for (const project of projects.data ?? []) {
      for (const featureId of project.deliveredFeatureIds) {
        const list = map.get(featureId) ?? [];
        list.push(project);
        map.set(featureId, list);
      }
    }
    return map;
  }, [projects.data]);

  const deleteFeature = useInvalidatingMutation(
    (id: string) => api.features.delete(id),
    () => [
      queryKeys.features(workspaceId ?? ""),
      queryKeys.projects(workspaceId ?? ""),
    ],
    { successMessage: "Feature deleted", onSuccess: () => setDeleting(null) },
  );

  return (
    <>
      <PageHeader
        title="Features"
        description="Product capabilities — the seam where delivery meets the market"
      >
        <Button
          onClick={() => {
            setEditing(null);
            setEditorOpen(true);
          }}
        >
          <PlusIcon data-icon="inline-start" />
          New feature
        </Button>
      </PageHeader>

      <div className="flex-1 overflow-auto p-6">
        {features.data && features.data.length === 0 ? (
          <EmptyState
            title="No features yet"
            hint="A feature is a capability, not a work item. Link it to the projects that make it real; its market meaning lives in coverage deltas."
          />
        ) : (
          <div className="overflow-hidden rounded-xl border bg-card">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Name</TableHead>
                  <TableHead>Status</TableHead>
                  <TableHead>Delivered by</TableHead>
                  <TableHead className="w-20">Deltas</TableHead>
                  <TableHead className="w-24 text-right">Actions</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {(features.data ?? []).map((feature) => (
                  <TableRow key={feature.id}>
                    <TableCell>
                      <div className="font-medium">{feature.name}</div>
                      {feature.description && (
                        <div className="max-w-96 truncate text-xs text-muted-foreground">
                          {feature.description}
                        </div>
                      )}
                    </TableCell>
                    <TableCell>
                      <Badge variant={STATUS_BADGE[feature.status]}>
                        {feature.status}
                      </Badge>
                    </TableCell>
                    <TableCell>
                      <div className="flex max-w-72 flex-wrap gap-1">
                        {(projectsByFeature.get(feature.id) ?? []).map((p) => (
                          <Badge key={p.id} variant="outline">
                            {p.name}
                          </Badge>
                        ))}
                        {(projectsByFeature.get(feature.id) ?? []).length ===
                          0 && (
                          <span className="text-xs text-muted-foreground">
                            unlinked
                          </span>
                        )}
                      </div>
                    </TableCell>
                    <TableCell className="font-mono text-xs text-muted-foreground">
                      {feature.deltas.length}
                    </TableCell>
                    <TableCell className="text-right">
                      <div className="inline-flex gap-1">
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-label={`Edit ${feature.name}`}
                          onClick={() => {
                            setEditing(feature);
                            setEditorOpen(true);
                          }}
                        >
                          <PencilIcon />
                        </Button>
                        <Button
                          variant="ghost"
                          size="icon-sm"
                          aria-label={`Delete ${feature.name}`}
                          onClick={() => setDeleting(feature)}
                        >
                          <Trash2Icon className="text-destructive" />
                        </Button>
                      </div>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          </div>
        )}
      </div>

      {workspaceId && (
        <FeatureEditorDialog
          key={editing?.id ?? "new"}
          open={editorOpen}
          onOpenChange={setEditorOpen}
          workspaceId={workspaceId}
          feature={editing}
          linkedProjects={
            editing ? (projectsByFeature.get(editing.id) ?? []) : []
          }
          allProjects={projects.data ?? []}
        />
      )}

      <ConfirmDialog
        open={deleting !== null}
        onOpenChange={(open) => !open && setDeleting(null)}
        title={`Delete ${deleting?.name ?? "feature"}?`}
        description="Project links to this feature are removed as well."
        onConfirm={() => deleting && deleteFeature.mutate(deleting.id)}
        busy={deleteFeature.isPending}
      />
    </>
  );
}

function FeatureEditorDialog({
  open,
  onOpenChange,
  workspaceId,
  feature,
  linkedProjects,
  allProjects,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  workspaceId: string;
  feature: FeatureDto | null;
  linkedProjects: ProjectDto[];
  allProjects: ProjectDto[];
}) {
  const [name, setName] = React.useState(feature?.name ?? "");
  const [description, setDescription] = React.useState(
    feature?.description ?? "",
  );
  const [status, setStatus] = React.useState<FeatureStatus>(
    feature?.status ?? "Hypothetical",
  );
  const [shippedDate, setShippedDate] = React.useState(
    feature?.shippedDate ?? "",
  );

  const invalidate = () => [
    queryKeys.features(workspaceId),
    queryKeys.projects(workspaceId),
  ];

  const save = useInvalidatingMutation(
    () =>
      feature
        ? api.features.update(feature.id, {
            name,
            description: description || null,
            productId: feature.productId,
            status,
            shippedDate: shippedDate || null,
          })
        : api.features.create({
            workspaceId,
            name,
            description: description || null,
          }),
    invalidate,
    {
      successMessage: feature ? "Feature saved" : "Feature created",
      onSuccess: () => onOpenChange(false),
    },
  );

  const link = useInvalidatingMutation(
    (projectId: string) =>
      api.projects.linkFeature(projectId, { featureId: feature!.id }),
    invalidate,
  );

  const unlink = useInvalidatingMutation(
    (projectId: string) => api.projects.unlinkFeature(projectId, feature!.id),
    invalidate,
  );

  const linkable = allProjects.filter(
    (p) => !linkedProjects.some((lp) => lp.id === p.id),
  );

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{feature ? "Edit feature" : "New feature"}</DialogTitle>
          <DialogDescription>
            {feature
              ? "Details, lifecycle, and the projects that deliver it."
              : "Name the capability; link projects after creating it."}
          </DialogDescription>
        </DialogHeader>
        <form
          className="flex flex-col gap-4"
          onSubmit={(e) => {
            e.preventDefault();
            save.mutate(undefined);
          }}
        >
          <div className="flex flex-col gap-2">
            <Label htmlFor="feat-name">Name</Label>
            <Input
              id="feat-name"
              value={name}
              onChange={(e) => setName(e.target.value)}
              required
              maxLength={200}
            />
          </div>
          <div className="flex flex-col gap-2">
            <Label htmlFor="feat-desc">Description</Label>
            <Textarea
              id="feat-desc"
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              rows={3}
              placeholder="What the product can do once this exists"
            />
          </div>
          {feature && (
            <div className="grid grid-cols-2 gap-3">
              <div className="flex flex-col gap-2">
                <Label>Status</Label>
                <Select
                  items={FEATURE_STATUSES.map((s) => ({ label: s, value: s }))}
                  value={status}
                  onValueChange={(v) => v && setStatus(v as FeatureStatus)}
                >
                  <SelectTrigger className="w-full">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {FEATURE_STATUSES.map((s) => (
                      <SelectItem key={s} value={s}>
                        {s}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="flex flex-col gap-2">
                <Label htmlFor="feat-shipped">Shipped date</Label>
                <Input
                  id="feat-shipped"
                  type="date"
                  value={shippedDate}
                  onChange={(e) => setShippedDate(e.target.value)}
                />
              </div>
            </div>
          )}

          {feature && (
            <div className="flex flex-col gap-2">
              <Label>Delivered by</Label>
              <div className="flex flex-wrap gap-1.5">
                {linkedProjects.map((p) => (
                  <Badge key={p.id} variant="secondary" className="gap-1 pr-1">
                    {p.name}
                    <button
                      type="button"
                      aria-label={`Unlink ${p.name}`}
                      className="rounded-sm p-0.5 hover:bg-foreground/10"
                      onClick={() => unlink.mutate(p.id)}
                    >
                      <XIcon className="size-3" />
                    </button>
                  </Badge>
                ))}
                {linkedProjects.length === 0 && (
                  <span className="text-xs text-muted-foreground">
                    No projects linked yet.
                  </span>
                )}
              </div>
              {linkable.length > 0 && (
                <Select
                  items={linkable.map((p) => ({ label: p.name, value: p.id }))}
                  value={null}
                  onValueChange={(v) => v && link.mutate(v as string)}
                >
                  <SelectTrigger className="w-full">
                    <SelectValue>
                      {() => "Link a project…"}
                    </SelectValue>
                  </SelectTrigger>
                  <SelectContent>
                    {linkable.map((p) => (
                      <SelectItem key={p.id} value={p.id}>
                        {p.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              )}
            </div>
          )}

          <DialogFooter className="mt-2">
            <DialogClose render={<Button variant="outline" />}>
              Cancel
            </DialogClose>
            <Button type="submit" disabled={save.isPending || !name.trim()}>
              {save.isPending
                ? "Saving…"
                : feature
                  ? "Save changes"
                  : "Create feature"}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}
