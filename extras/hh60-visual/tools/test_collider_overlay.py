"""Synthetic mesh checks for the actual collider wireframe exporter.

No game assets, output payloads, or Unity runtime are used.
"""
import math
import unittest

import numpy as np

from export_collider_overlay import extract_edges


def edge(first, second):
    return tuple(sorted((tuple(first), tuple(second))))


class ColliderOverlayTests(unittest.TestCase):
    quad = [(0, 0, 0), (1, 0, 0), (1, 1, 0), (0, 1, 0)]
    quad_faces = [(0, 1, 2), (0, 2, 3)]

    def edges(self, vertices, triangles, **options):
        flattened = extract_edges(vertices, triangles, **options)
        self.assertEqual(len(flattened) % 6, 0)
        result = [edge(flattened[index:index + 3], flattened[index + 3:index + 6])
                  for index in range(0, len(flattened), 6)]
        self.assertEqual(len(result), len(set(result)), "Repeated wireframe segment")
        self.assertTrue(all(first != second for first, second in result))
        return set(result)

    def quad_boundary(self):
        return {edge(self.quad[index], self.quad[(index + 1) % 4]) for index in range(4)}

    def test_single_triangle_keeps_all_boundary_edges(self):
        vertices = self.quad[:3]
        self.assertEqual(self.edges(vertices, [(0, 1, 2)]),
                         {edge(vertices[0], vertices[1]), edge(vertices[1], vertices[2]),
                          edge(vertices[2], vertices[0])})

    def test_coplanar_quad_omits_internal_diagonal(self):
        self.assertEqual(self.edges(self.quad, self.quad_faces), self.quad_boundary())

    def test_duplicate_vertex_seam_welds_without_an_internal_diagonal(self):
        vertices = [self.quad[index] for index in (0, 1, 2, 0, 2, 3)]
        self.assertEqual(self.edges(vertices, [(0, 1, 2), (3, 4, 5)]), self.quad_boundary())

    def test_nearby_but_distinct_positions_are_not_rounded_together(self):
        # A tiny real gap is still a collision boundary, not a duplicate seam.
        shifted = [(x, y, 1e-10) for x, y, _ in (self.quad[0], self.quad[2], self.quad[3])]
        vertices = self.quad[:3] + shifted
        result = self.edges(vertices, [(0, 1, 2), (3, 4, 5)])
        self.assertEqual(len(result), 6)
        self.assertIn(edge(shifted[0], shifted[1]), result)
        self.assertIn(edge(self.quad[0], self.quad[2]), result)

    def test_right_angle_crease_keeps_shared_edge(self):
        vertices = [(0, 0, 0), (1, 0, 0), (0, 1, 0), (0, 0, 1)]
        result = self.edges(vertices, [(0, 1, 2), (1, 0, 3)])
        self.assertEqual(len(result), 5)
        self.assertIn(edge(vertices[0], vertices[1]), result)

    def test_sharp_angle_threshold_changes_only_the_shared_crease(self):
        angle = math.radians(10)
        vertices = [(0, 0, 0), (1, 0, 0), (0, 1, 0),
                    (0, -math.cos(angle), math.sin(angle))]
        faces = [(0, 1, 2), (1, 0, 3)]
        smooth = self.edges(vertices, faces, sharp_degrees=15)
        sharp = self.edges(vertices, faces, sharp_degrees=5)
        self.assertEqual(len(smooth), 4)
        self.assertEqual(sharp - smooth, {edge(vertices[0], vertices[1])})

    def test_nonmanifold_shared_edge_is_retained_even_when_faces_are_coplanar(self):
        vertices = [(0, 0, 0), (1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 2, 0)]
        result = self.edges(vertices, [(0, 1, 2), (1, 0, 3), (0, 1, 4)])
        self.assertEqual(len(result), 7)
        self.assertIn(edge(vertices[0], vertices[1]), result)

    def test_collinear_degenerate_face_preserves_nonzero_edges(self):
        vertices = [(0, 0, 0), (1, 0, 0), (2, 0, 0)]
        self.assertEqual(len(self.edges(vertices, [(0, 1, 2)])), 3)

    def test_collapsed_face_does_not_duplicate_or_emit_zero_length_edges(self):
        vertices = [(0, 0, 0), (1, 0, 0)]
        self.assertEqual(self.edges(vertices, [(0, 1, 1)]), {edge(*vertices)})

    def test_invalid_vertex_shapes_are_rejected(self):
        for vertices in ([], [0, 1, 2], [(0, 1)], [(0, 1, 2, 3)]):
            with self.subTest(vertices=vertices), self.assertRaises(ValueError):
                extract_edges(vertices, [(0, 0, 0)])

    def test_nonfinite_vertices_are_rejected(self):
        for value in (math.nan, math.inf, -math.inf):
            vertices = [(value, 0, 0), (1, 0, 0), (0, 1, 0)]
            with self.subTest(value=value), self.assertRaisesRegex(ValueError, "Nonfinite"):
                extract_edges(vertices, [(0, 1, 2)])

    def test_invalid_triangle_shapes_and_indices_are_rejected(self):
        invalid = [[], [0, 1, 2], [(0, 1)], [(0, 1, 2, 3)],
                   [(-1, 1, 2)], [(0, 1, 4)], [(0.0, 1.0, 2.0)],
                   [(0, 1, math.nan)], [(False, True, True)]]
        for triangles in invalid:
            with self.subTest(triangles=triangles), self.assertRaises(ValueError):
                extract_edges(self.quad, triangles)

    def test_invalid_angle_and_edge_limit_options_are_rejected(self):
        invalid = [dict(sharp_degrees=value) for value in (0, -1, 180, math.nan, math.inf)]
        invalid += [dict(max_edges=value) for value in (-1, 1.5, "4")]
        for options in invalid:
            with self.subTest(options=options), self.assertRaises(ValueError):
                extract_edges(self.quad, self.quad_faces, **options)

    def test_exact_edge_budget_succeeds(self):
        self.assertEqual(self.edges(self.quad, self.quad_faces, max_edges=4), self.quad_boundary())

    def test_insufficient_edge_budget_fails_instead_of_returning_truncated_geometry(self):
        for maximum in (0, 3):
            with self.subTest(maximum=maximum), self.assertRaisesRegex(ValueError, "refusing to truncate"):
                extract_edges(self.quad, self.quad_faces, max_edges=maximum)

    def test_vertex_and_face_order_do_not_change_exported_geometry(self):
        vertices = np.asarray(self.quad)
        faces = np.asarray(self.quad_faces)
        expected = extract_edges(vertices, faces)
        permutation = np.asarray([2, 0, 3, 1])
        remap = np.argsort(permutation)
        self.assertEqual(extract_edges(vertices[permutation], remap[faces[::-1]]), expected)
        np.testing.assert_array_equal(vertices, self.quad)
        np.testing.assert_array_equal(faces, self.quad_faces)


if __name__ == "__main__":
    unittest.main()
