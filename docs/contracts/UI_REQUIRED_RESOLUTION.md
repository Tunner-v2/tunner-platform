# Validated UI_REQUIRED resolution

P0-RV007 implements the approved Document10 design/approve/unblock sequence and Document03 ordinary lifecycle without a general side-state override.

The native advisory transition UI_REQUIRED -> REFINEMENT is allowed only for repository-changing work with recorded RESOLVED_EXISTING_AUTHORITY, scope-bound committed current UI evidence, passing ordinary work/role/source/authority/Revision gates, and no missing, ambiguous or unresolved linked TODO/defect. This bounded path covers already implemented and validated UI corrections.

It grants no shortcut to DONE, no Product/release approval, and no exit for other side states. The normal refinement,ready,in-progress,review,validation and applicable Product acceptance sequence remains required. Unimplemented designs still use their owning governed preparation; this correction does not waive design or implementation gates.
